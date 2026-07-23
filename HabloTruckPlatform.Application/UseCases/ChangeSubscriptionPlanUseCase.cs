using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class ChangeSubscriptionPlanUseCase
{
    private const string OperationName = "subscription_plan_change";
    private const string PlanMonthly = "individual_monthly";
    private const string PlanYearly = "individual_yearly";
    private const string EffectiveImmediate = "immediate";
    private const string EffectivePeriodEnd = "period_end";

    private readonly IUserStore _users;
    private readonly IStripeSubscriptionGateway _stripeSubscriptions;
    private readonly StripeOptions _stripeOptions;
    private readonly IClock _clock;
    private readonly IAppMetrics? _metrics;
    private readonly IUserResolver? _userResolver;
    private readonly IAdminPaymentAlertNotifier? _adminPaymentAlerts;
    private readonly ILogger<ChangeSubscriptionPlanUseCase> _logger;

    public ChangeSubscriptionPlanUseCase(
        IUserStore users,
        IStripeSubscriptionGateway stripeSubscriptions,
        StripeOptions stripeOptions,
        IClock clock,
        ILogger<ChangeSubscriptionPlanUseCase>? logger = null,
        IAppMetrics? metrics = null,
        IUserResolver? userResolver = null,
        IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        _users = users;
        _stripeSubscriptions = stripeSubscriptions;
        _stripeOptions = stripeOptions;
        _clock = clock;
        _logger = logger ?? NullLogger<ChangeSubscriptionPlanUseCase>.Instance;
        _metrics = metrics;
        _userResolver = userResolver;
        _adminPaymentAlerts = adminPaymentAlerts;
    }

    public async Task<ChangeSubscriptionPlanResult> ExecuteAsync(
        ChangeSubscriptionPlanRequest request,
        CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();
        var nowUtc = _clock.UtcNow;
        var requestedTarget = NormalizePlanType(request?.TargetPlanType);
        var requestedEffectiveWhen = NormalizeEffectiveWhen(request?.EffectiveWhen);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = OperationName,
            ["UserId"] = request?.ActorUserId,
            ["ManyChatSubscriberId"] = request?.ManyChatSubscriberId,
            ["SubscriptionId"] = request?.SubscriptionId,
            ["TargetPlanType"] = requestedTarget,
            ["EffectiveWhen"] = requestedEffectiveWhen
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} TargetPlanType={TargetPlanType} EffectiveWhen={EffectiveWhen}",
            "entry",
            OperationName,
            requestedTarget,
            requestedEffectiveWhen);

        if (request is null)
            return Fail("invalid_request", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen);

        var actorRef = await ResolveActorRefAsync(request, ct);
        if (actorRef is null)
            return Fail(
                HasActorResolutionInput(request) ? "actor_not_found" : "actor_required",
                opWatch,
                nowUtc,
                requestedTarget,
                requestedEffectiveWhen);

        var actorPk = actorRef.Value.UserPk;
        var actorId = actorRef.Value.UserId;

        if (requestedTarget is not (PlanMonthly or PlanYearly))
            return Fail("invalid_target_plan_type", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, actorUserPk: actorPk, actorUserId: actorId);

        var actorWatch = Stopwatch.StartNew();
        var user = await _users.GetAsync(actorPk, actorId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "user.get",
            "Users",
            actorWatch.ElapsedMilliseconds,
            user is not null);

        if (user is null)
            return Fail("actor_not_found", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, actorUserPk: actorPk, actorUserId: actorId);

        var subscriptionId = NullIfBlank(request.SubscriptionId) ?? NullIfBlank(user.StripeSubscriptionId);
        if (subscriptionId is null)
            return Fail("subscription_id_required", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, actorUserPk: actorPk, actorUserId: actorId);

        if (!string.IsNullOrWhiteSpace(request.SubscriptionId)
            && !string.IsNullOrWhiteSpace(user.StripeSubscriptionId)
            && !string.Equals(user.StripeSubscriptionId, request.SubscriptionId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);
        }

        var targetPriceId = ResolvePriceId(requestedTarget);
        if (targetPriceId is null)
            return Fail("price_id_not_configured_for_plan", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);

        var readWatch = Stopwatch.StartNew();
        var current = await _stripeSubscriptions.GetSubscriptionAsync(subscriptionId, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "get_subscription",
            "Stripe API",
            readWatch.ElapsedMilliseconds,
            true,
            current is not null,
            subscriptionId);

        if (current is null)
            return Fail("subscription_not_found", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId)
            && !string.Equals(user.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);
        }

        var currentPlan = DerivePlanTypeFromPriceId(current.PriceId);
        if (currentPlan is null)
            return Fail("current_subscription_not_individual", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);

        if (string.Equals(currentPlan, requestedTarget, StringComparison.OrdinalIgnoreCase))
        {
            if (requestedEffectiveWhen is not null && requestedEffectiveWhen is not (EffectiveImmediate or EffectivePeriodEnd))
                return Fail("invalid_effective_when", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} CurrentPlanType={CurrentPlanType} TargetPlanType={TargetPlanType} DurationMs={DurationMs}",
                "outcome",
                "no_action_needed",
                "target_plan_already_active",
                currentPlan,
                requestedTarget,
                opWatch.ElapsedMilliseconds);

            _metrics?.SubscriptionPlanChange("no_action_needed", "target_plan_already_active", requestedTarget, requestedEffectiveWhen ?? "-");

            return Success(current, actorPk, actorId, currentPlan, requestedTarget, requestedEffectiveWhen ?? EffectiveImmediate, nowUtc);
        }

        var effectiveWhen = ResolveEffectiveWhen(requestedTarget, requestedEffectiveWhen);
        if (effectiveWhen is null)
            return Fail("invalid_effective_when", opWatch, nowUtc, requestedTarget, requestedEffectiveWhen, subscriptionId, actorPk, actorId);

        StripeSubscriptionSnapshot? updated;
        try
        {
            var updateWatch = Stopwatch.StartNew();
            if (requestedTarget == PlanYearly)
            {
                updated = await _stripeSubscriptions.ChangeSubscriptionPriceAsync(
                    subscriptionId,
                    targetPriceId,
                    "always_invoice",
                    "now",
                    BuildIdempotencyKey(subscriptionId, requestedTarget, effectiveWhen),
                    ct);
            }
            else
            {
                updated = await _stripeSubscriptions.ScheduleSubscriptionPriceChangeAtPeriodEndAsync(
                    subscriptionId,
                    targetPriceId,
                    BuildIdempotencyKey(subscriptionId, requestedTarget, effectiveWhen),
                    ct);
            }

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId} TargetPlanType={TargetPlanType} EffectiveWhen={EffectiveWhen}",
                "dependency",
                "stripe",
                requestedTarget == PlanYearly ? "change_subscription_price" : "schedule_subscription_price_change",
                "Stripe API",
                updateWatch.ElapsedMilliseconds,
                true,
                updated is not null,
                subscriptionId,
                requestedTarget,
                effectiveWhen);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} SubscriptionId={SubscriptionId} TargetPlanType={TargetPlanType}",
                "exception",
                "dependency_failed",
                "stripe",
                "change_subscription_price",
                subscriptionId,
                requestedTarget);

            await NotifyPlanChangeFailureAsync(user, subscriptionId, requestedTarget, targetPriceId, effectiveWhen, "stripe_update_failed", ct);
            return Fail("stripe_update_failed", opWatch, nowUtc, requestedTarget, effectiveWhen, subscriptionId, actorPk, actorId);
        }

        if (updated is null)
        {
            await NotifyPlanChangeFailureAsync(user, subscriptionId, requestedTarget, targetPriceId, effectiveWhen, "stripe_update_returned_null", ct);
            return Fail("stripe_update_failed", opWatch, nowUtc, requestedTarget, effectiveWhen, subscriptionId, actorPk, actorId);
        }

        if (requestedTarget == PlanYearly)
            ApplyLocalSubscriptionHint(user, updated, requestedTarget, nowUtc);
        else
            ApplyCurrentSubscriptionHint(user, updated, nowUtc);

        var writeWatch = Stopwatch.StartNew();
        await _users.UpsertAsync(user, ct);
        await _users.UpsertLookupsAsync(user, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success} UserId={UserId}",
            "persistence",
            "user.upsert_subscription_plan_change",
            "Users+Lookups",
            writeWatch.ElapsedMilliseconds,
            true,
            user.UserId);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} PreviousPlanType={PreviousPlanType} TargetPlanType={TargetPlanType} EffectiveWhen={EffectiveWhen} SubscriptionId={SubscriptionId} PriceId={PriceId} DurationMs={DurationMs}",
            "outcome",
            "completed",
            requestedTarget == PlanYearly ? "individual_upgrade_applied" : "individual_downgrade_scheduled_period_end",
            currentPlan,
            requestedTarget,
            effectiveWhen,
            updated.SubscriptionId,
            updated.PriceId,
            opWatch.ElapsedMilliseconds);

        _metrics?.SubscriptionPlanChange(
            "completed",
            requestedTarget == PlanYearly ? "individual_upgrade_applied" : "individual_downgrade_scheduled_period_end",
            requestedTarget,
            effectiveWhen);

        return Success(updated, actorPk, actorId, currentPlan, requestedTarget, effectiveWhen, nowUtc);
    }

    private async Task NotifyPlanChangeFailureAsync(
        User user,
        string subscriptionId,
        string? targetPlanType,
        string targetPriceId,
        string? effectiveWhen,
        string reason,
        CancellationToken ct)
    {
        if (_adminPaymentAlerts is null)
            return;

        await _adminPaymentAlerts.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = OperationName,
            FailureStage = "subscription_plan_change",
            FailureReason = reason,
            Severity = "High",
            OccurredAtUtc = _clock.UtcNow,
            UserPk = Buckets.UserBucketPk(user.UserId),
            UserId = user.UserId,
            Email = user.EmailNormalized,
            PhoneE164 = user.PhoneE164,
            ManyChatSubscriberId = user.ManyChatSubscriberId,
            CompanyId = user.CompanyId,
            StripeCustomerId = user.StripeCustomerId,
            StripeSubscriptionId = subscriptionId,
            PlanType = targetPlanType,
            PriceId = targetPriceId,
            Details =
            {
                ["currentPlanType"] = user.PlanType,
                ["currentStripePriceId"] = user.StripePriceId,
                ["effectiveWhen"] = effectiveWhen,
                ["subscriptionStatus"] = user.SubscriptionStatus
            }
        }, ct);
    }

    private ChangeSubscriptionPlanResult Fail(
        string error,
        Stopwatch opWatch,
        DateTimeOffset requestedAtUtc,
        string? targetPlanType,
        string? effectiveWhen,
        string? subscriptionId = null,
        string? actorUserPk = null,
        string? actorUserId = null)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} TargetPlanType={TargetPlanType} EffectiveWhen={EffectiveWhen} SubscriptionId={SubscriptionId} DurationMs={DurationMs}",
            "outcome",
            error == "forbidden" ? "denied" : "validation_failed",
            error,
            targetPlanType,
            effectiveWhen,
            subscriptionId,
            opWatch.ElapsedMilliseconds);

        _metrics?.SubscriptionPlanChange("failed", error, targetPlanType ?? "-", effectiveWhen ?? "-");

        return new ChangeSubscriptionPlanResult
        {
            Result = false,
            Error = error,
            ActorUserPk = actorUserPk,
            ActorUserId = actorUserId,
            SubscriptionId = subscriptionId,
            TargetPlanType = targetPlanType,
            EffectiveWhen = effectiveWhen,
            RequestedAtUtc = requestedAtUtc
        };
    }

    private static ChangeSubscriptionPlanResult Success(
        StripeSubscriptionSnapshot snapshot,
        string actorUserPk,
        string actorUserId,
        string? previousPlanType,
        string targetPlanType,
        string effectiveWhen,
        DateTimeOffset requestedAtUtc)
        => new()
        {
            Result = true,
            ActorUserPk = actorUserPk,
            ActorUserId = actorUserId,
            SubscriptionId = snapshot.SubscriptionId,
            PreviousPlanType = previousPlanType,
            TargetPlanType = targetPlanType,
            EffectiveWhen = effectiveWhen,
            StripePriceId = snapshot.PriceId,
            Interval = snapshot.Interval,
            SubscriptionStatus = snapshot.Status,
            CurrentPeriodEndUtc = snapshot.CurrentPeriodEndUtc,
            ScheduledChangeEffectiveAtUtc = effectiveWhen == EffectivePeriodEnd ? snapshot.CurrentPeriodEndUtc : null,
            CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd,
            RequestedAtUtc = requestedAtUtc,
            Error = null
        };

    private async Task<UserRef?> ResolveActorRefAsync(ChangeSubscriptionPlanRequest request, CancellationToken ct)
    {
        var actorPk = NullIfBlank(request.ActorUserPk);
        var actorId = NullIfBlank(request.ActorUserId);
        if (actorPk is not null && actorId is not null)
            return new UserRef(actorPk, actorId);

        var manyChatSubscriberId = NullIfBlank(request.ManyChatSubscriberId);
        var email = NormalizeEmail(request.EmailNormalized) ?? NormalizeEmail(request.Email);
        var phone = NullIfBlank(request.PhoneE164);

        if (manyChatSubscriberId is null && email is null && phone is null)
            return null;

        if (_userResolver is null)
            return null;

        UserRef? resolved = null;

        if (manyChatSubscriberId is not null)
            resolved = await _userResolver.ResolveByManyChatSubscriberIdAsync(manyChatSubscriberId, ct);

        if (resolved is null && email is not null)
            resolved = await _userResolver.ResolveByEmailNormalizedAsync(email, ct);

        if (resolved is null && phone is not null)
            resolved = await _userResolver.ResolveByPhoneE164Async(phone, ct);

        return resolved;
    }

    private static bool HasActorResolutionInput(ChangeSubscriptionPlanRequest request)
        => NullIfBlank(request.ActorUserPk) is not null
           || NullIfBlank(request.ActorUserId) is not null
           || NullIfBlank(request.ManyChatSubscriberId) is not null
           || NormalizeEmail(request.EmailNormalized) is not null
           || NormalizeEmail(request.Email) is not null
           || NullIfBlank(request.PhoneE164) is not null;

    private void ApplyLocalSubscriptionHint(
        User user,
        StripeSubscriptionSnapshot snapshot,
        string targetPlanType,
        DateTimeOffset nowUtc)
    {
        user.StripeSubscriptionId = snapshot.SubscriptionId;
        user.StripeCustomerId = string.IsNullOrWhiteSpace(user.StripeCustomerId) ? snapshot.CustomerId : user.StripeCustomerId;
        user.StripePriceId = snapshot.PriceId;
        user.SubscriptionStatus = snapshot.Status;
        user.StripeCancelAtPeriodEnd = snapshot.CancelAtPeriodEnd;
        user.StripeCurrentPeriodEndUtc = snapshot.CurrentPeriodEndUtc;
        user.IndividualPlanTerm = targetPlanType == PlanYearly ? "annual" : "monthly";
        user.PlanType = targetPlanType;
        user.UpdatedAtUtc = nowUtc;
    }

    private void ApplyCurrentSubscriptionHint(
        User user,
        StripeSubscriptionSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        user.StripeSubscriptionId = snapshot.SubscriptionId;
        user.StripeCustomerId = string.IsNullOrWhiteSpace(user.StripeCustomerId) ? snapshot.CustomerId : user.StripeCustomerId;
        user.StripePriceId = snapshot.PriceId;
        user.SubscriptionStatus = snapshot.Status;
        user.StripeCancelAtPeriodEnd = snapshot.CancelAtPeriodEnd;
        user.StripeCurrentPeriodEndUtc = snapshot.CurrentPeriodEndUtc;
        user.UpdatedAtUtc = nowUtc;
    }

    private string? ResolvePriceId(string planType)
        => planType switch
        {
            PlanMonthly => NullIfBlank(_stripeOptions.IndividualMonthlyPriceId),
            PlanYearly => NullIfBlank(_stripeOptions.IndividualYearlyPriceId),
            _ => null
        };

    private string? DerivePlanTypeFromPriceId(string? priceId)
    {
        var p = NullIfBlank(priceId);
        if (p is null) return null;

        if (p == _stripeOptions.IndividualMonthlyPriceId) return PlanMonthly;
        if (p == _stripeOptions.IndividualYearlyPriceId) return PlanYearly;

        return null;
    }

    private static string? ResolveEffectiveWhen(string targetPlan, string? requested)
    {
        var defaulted = requested ?? (targetPlan == PlanYearly ? EffectiveImmediate : EffectivePeriodEnd);

        if (targetPlan == PlanYearly && defaulted == EffectiveImmediate)
            return EffectiveImmediate;

        if (targetPlan == PlanMonthly && defaulted == EffectivePeriodEnd)
            return EffectivePeriodEnd;

        return null;
    }

    private static string? NormalizePlanType(string? value)
    {
        var p = NullIfBlank(value)?.ToLowerInvariant();
        return p switch
        {
            "monthly" or "individual_monthly" => PlanMonthly,
            "yearly" or "annual" or "individual_yearly" => PlanYearly,
            _ => p
        };
    }

    private static string? NormalizeEffectiveWhen(string? value)
    {
        var p = NullIfBlank(value)?.ToLowerInvariant();
        return p switch
        {
            null => null,
            "now" or "immediate" => EffectiveImmediate,
            "renewal" or "period_end" or "period-end" or "next_invoice" or "next-invoice" => EffectivePeriodEnd,
            _ => p
        };
    }

    private static string BuildIdempotencyKey(string subscriptionId, string targetPlanType, string effectiveWhen)
        => $"sub-change-plan:{subscriptionId.Trim()}:{targetPlanType}:{effectiveWhen}";

    private static string? NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
