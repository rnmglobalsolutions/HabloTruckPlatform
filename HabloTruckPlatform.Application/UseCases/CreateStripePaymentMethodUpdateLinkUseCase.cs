using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class CreateStripePaymentMethodUpdateLinkUseCase
{
    private readonly IUserStore _users;
    private readonly IStripeSubscriptionGateway _stripeSubscriptions;
    private readonly IStripeAdminClient _stripeAdmin;
    private readonly IClock _clock;
    private readonly IAdminPaymentAlertNotifier? _adminPaymentAlerts;
    private readonly ILogger<CreateStripePaymentMethodUpdateLinkUseCase> _logger;

    public CreateStripePaymentMethodUpdateLinkUseCase(
        IUserStore users,
        IStripeSubscriptionGateway stripeSubscriptions,
        IStripeAdminClient stripeAdmin,
        IClock clock,
        ILogger<CreateStripePaymentMethodUpdateLinkUseCase>? logger = null,
        IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        _users = users;
        _stripeSubscriptions = stripeSubscriptions;
        _stripeAdmin = stripeAdmin;
        _clock = clock;
        _adminPaymentAlerts = adminPaymentAlerts;
        _logger = logger ?? NullLogger<CreateStripePaymentMethodUpdateLinkUseCase>.Instance;
    }

    public async Task<StripePaymentMethodUpdateLinkResult> ExecuteAsync(
        StripePaymentMethodUpdateLinkRequest request,
        CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            "entry",
            "stripe_payment_method_update_link");

        if (request is null)
            return Fail("invalid_request", opWatch, null, null);

        var actorPk = NullIfBlank(request.ActorUserPk);
        var actorId = NullIfBlank(request.ActorUserId);
        if (actorPk is null || actorId is null)
            return Fail("actor_required", opWatch, actorId, request.SubscriptionId);

        var returnUrl = NormalizeAbsoluteHttpUrl(request.ReturnUrl);
        if (returnUrl is null)
            return Fail("invalid_return_url", opWatch, actorId, request.SubscriptionId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "stripe_payment_method_update_link",
            ["UserId"] = actorId,
            ["SubscriptionId"] = request.SubscriptionId
        });

        var userWatch = Stopwatch.StartNew();
        var actor = await _users.GetAsync(actorPk, actorId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "user.get",
            "Users",
            userWatch.ElapsedMilliseconds,
            actor is not null);

        if (actor is null)
            return Fail("actor_not_found", opWatch, actorId, request.SubscriptionId);

        var explicitSubscriptionId = NullIfBlank(request.SubscriptionId);
        if (explicitSubscriptionId is not null
            && !string.IsNullOrWhiteSpace(actor.StripeSubscriptionId)
            && !string.Equals(actor.StripeSubscriptionId, explicitSubscriptionId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", opWatch, actorId, explicitSubscriptionId);
        }

        var subscriptionId = explicitSubscriptionId ?? NullIfBlank(actor.StripeSubscriptionId);
        if (subscriptionId is null)
            return Fail("subscription_id_required", opWatch, actorId, explicitSubscriptionId);

        var stripeWatch = Stopwatch.StartNew();
        var current = await _stripeSubscriptions.GetSubscriptionAsync(subscriptionId, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
            "dependency",
            "stripe",
            "get_subscription",
            "Stripe API",
            stripeWatch.ElapsedMilliseconds,
            true,
            current is not null);

        if (current is null)
            return Fail("subscription_not_found", opWatch, actorId, subscriptionId);

        if (!string.IsNullOrWhiteSpace(actor.StripeCustomerId)
            && !string.Equals(actor.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", opWatch, actorId, subscriptionId);
        }

        var customerId = NullIfBlank(actor.StripeCustomerId) ?? NullIfBlank(current.CustomerId);
        if (customerId is null)
            return Fail("customer_id_required", opWatch, actorId, subscriptionId);

        try
        {
            var portalWatch = Stopwatch.StartNew();
            var session = await _stripeAdmin.CreatePaymentMethodUpdateSessionAsync(
                customerId,
                subscriptionId,
                returnUrl,
                ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} SessionId={SessionId}",
                "dependency",
                "stripe",
                "create_payment_method_update_session",
                "Stripe API",
                portalWatch.ElapsedMilliseconds,
                !string.IsNullOrWhiteSpace(session.Url),
                session.SessionId);

            var result = new StripePaymentMethodUpdateLinkResult
            {
                Result = !string.IsNullOrWhiteSpace(session.Url),
                Url = session.Url,
                SessionId = session.SessionId,
                CustomerId = session.CustomerId,
                SubscriptionId = session.SubscriptionId,
                ImmediateRetryRecommended = true,
                Error = string.IsNullOrWhiteSpace(session.Url) ? "stripe_portal_session_failed" : null
            };

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                "outcome",
                result.Result ? "completed" : "dependency_failed",
                result.Result ? "payment_method_update_link_created" : result.Error,
                opWatch.ElapsedMilliseconds);

            if (!result.Result)
                await NotifyPortalFailureAsync(actor, customerId, subscriptionId, "stripe_portal_session_failed", ct);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation}",
                "exception",
                "dependency_failed",
                "stripe",
                "create_payment_method_update_session");

            await NotifyPortalFailureAsync(actor, customerId, subscriptionId, "stripe_portal_session_failed", ct);

            return Fail("stripe_portal_session_failed", opWatch, actorId, subscriptionId);
        }
    }

    private async Task NotifyPortalFailureAsync(
        User actor,
        string customerId,
        string subscriptionId,
        string reason,
        CancellationToken ct)
    {
        if (_adminPaymentAlerts is null)
            return;

        await _adminPaymentAlerts.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_payment_method_update_link",
            FailureStage = "billing_portal_session_creation",
            FailureReason = reason,
            Severity = "High",
            OccurredAtUtc = _clock.UtcNow,
            UserPk = Buckets.UserBucketPk(actor.UserId),
            UserId = actor.UserId,
            Email = actor.EmailNormalized,
            PhoneE164 = actor.PhoneE164,
            ManyChatSubscriberId = actor.ManyChatSubscriberId,
            CompanyId = actor.CompanyId,
            StripeCustomerId = customerId,
            StripeSubscriptionId = subscriptionId,
            PlanType = actor.PlanType,
            PriceId = actor.StripePriceId,
            Details =
            {
                ["paymentRecoveryStartedAtUtc"] = actor.PaymentRecoveryStartedAtUtc?.UtcDateTime.ToString("O"),
                ["subscriptionStatus"] = actor.SubscriptionStatus
            }
        }, ct);
    }

    private StripePaymentMethodUpdateLinkResult Fail(
        string error,
        Stopwatch opWatch,
        string? actorId,
        string? subscriptionId)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} UserId={UserId} SubscriptionId={SubscriptionId} DurationMs={DurationMs}",
            "outcome",
            error == "forbidden" ? "denied" : "validation_failed",
            error,
            actorId,
            subscriptionId,
            opWatch.ElapsedMilliseconds);

        return new StripePaymentMethodUpdateLinkResult
        {
            Result = false,
            Error = error
        };
    }

    private static string? NormalizeAbsoluteHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.ToString();
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
