using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class RetryStripeOpenInvoiceUseCase
{
    private readonly IUserStore _users;
    private readonly IStripeSubscriptionGateway _stripeSubscriptions;
    private readonly IStripeAdminClient _stripeAdmin;
    private readonly BillingRecoveryManyChatNotifier _billingRecoveryNotifier;
    private readonly IClock _clock;
    private readonly ILogger<RetryStripeOpenInvoiceUseCase> _logger;

    public RetryStripeOpenInvoiceUseCase(
        IUserStore users,
        IStripeSubscriptionGateway stripeSubscriptions,
        IStripeAdminClient stripeAdmin,
        BillingRecoveryManyChatNotifier billingRecoveryNotifier,
        IClock clock,
        ILogger<RetryStripeOpenInvoiceUseCase>? logger = null)
    {
        _users = users;
        _stripeSubscriptions = stripeSubscriptions;
        _stripeAdmin = stripeAdmin;
        _billingRecoveryNotifier = billingRecoveryNotifier;
        _clock = clock;
        _logger = logger ?? NullLogger<RetryStripeOpenInvoiceUseCase>.Instance;
    }

    public async Task<StripeRetryOpenInvoiceResult> ExecuteAsync(
        StripeRetryOpenInvoiceRequest request,
        CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            "entry",
            "stripe_retry_open_invoice");

        if (request is null)
            return Fail("invalid_request", opWatch, null, null);

        var actorPk = NullIfBlank(request.ActorUserPk);
        var actorId = NullIfBlank(request.ActorUserId);
        if (actorPk is null || actorId is null)
            return Fail("actor_required", opWatch, actorId, request.SubscriptionId);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "stripe_retry_open_invoice",
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
            var retryWatch = Stopwatch.StartNew();
            var attempt = await _stripeAdmin.RetryOpenInvoiceAsync(customerId, subscriptionId, ct);

            if (IsActivePaymentRecovery(actor))
                await _billingRecoveryNotifier.NotifyRetryOutcomeAsync(actor, attempt, actor.UserId, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} InvoiceId={InvoiceId}",
                "dependency",
                "stripe",
                "retry_open_invoice",
                "Stripe API",
                retryWatch.ElapsedMilliseconds,
                attempt.InvoiceFound,
                attempt.InvoiceId);

            var result = new StripeRetryOpenInvoiceResult
            {
                Result = true,
                CustomerId = attempt.CustomerId,
                SubscriptionId = attempt.SubscriptionId,
                InvoiceId = attempt.InvoiceId,
                InvoiceStatus = attempt.InvoiceStatus,
                CollectionMethod = attempt.CollectionMethod,
                InvoiceFound = attempt.InvoiceFound,
                PaymentAttempted = attempt.PaymentAttempted,
                InvoicePaid = attempt.InvoicePaid
            };

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} InvoiceId={InvoiceId} DurationMs={DurationMs}",
                "outcome",
                "completed",
                attempt.InvoicePaid ? "invoice_payment_attempted" : (attempt.InvoiceFound ? "invoice_retry_not_attempted" : "open_invoice_not_found"),
                attempt.InvoiceId,
                opWatch.ElapsedMilliseconds);

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
                "retry_open_invoice");

            return Fail("stripe_retry_failed", opWatch, actorId, subscriptionId);
        }
    }

    private StripeRetryOpenInvoiceResult Fail(
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

        return new StripeRetryOpenInvoiceResult
        {
            Result = false,
            Error = error
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsActivePaymentRecovery(User user)
    {
        if (user.PaymentRecoveryStartedAtUtc is null)
            return false;

        var status = user.SubscriptionStatus?.Trim().ToLowerInvariant();
        return status is "past_due" or "payment_failed" or "unpaid" or "incomplete" or "incomplete_expired";
    }
}
