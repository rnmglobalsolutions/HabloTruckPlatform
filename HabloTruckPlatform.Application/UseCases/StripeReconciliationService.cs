using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class StripeReconciliationService
{
    private const string OperationName = "stripe_reconciliation";

    private readonly IUserStore _userStore;
    private readonly IStripeAdminClient _stripeAdminClient;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly IClock _clock;
    private readonly GracePolicy _individualGracePolicy;
    private readonly ILogger<StripeReconciliationService> _logger;

    public StripeReconciliationService(
        IUserStore userStore,
        IStripeAdminClient stripeAdminClient,
        IGraceIndexStore graceIndexStore,
        AccessOrchestrator accessOrchestrator,
        IClock clock,
        GracePolicy individualGracePolicy,
        ILogger<StripeReconciliationService>? logger = null)
    {
        _userStore = userStore;
        _stripeAdminClient = stripeAdminClient;
        _ = graceIndexStore;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
        _individualGracePolicy = individualGracePolicy;
        _logger = logger ?? NullLogger<StripeReconciliationService>.Instance;
    }

    public async Task RunAsync(int take = 500, CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} Take={Take}",
            "entry",
            OperationName,
            take);

        var queryWatch = Stopwatch.StartNew();
        var users = await _userStore.QueryUsersWithStripeAsync(take, ct);
        var nowUtc = _clock.UtcNow;

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} UserCount={UserCount}",
            "persistence",
            "users.query_with_stripe",
            "Users",
            queryWatch.ElapsedMilliseconds,
            users.Count);

        var scanned = 0;
        var reconciled = 0;
        var skippedNoSubscription = 0;
        var skippedMissingStripe = 0;
        var skippedStale = 0;

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            using var userScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["OperationName"] = OperationName,
                ["UserId"] = user.UserId,
                ["StripeCustomerId"] = user.StripeCustomerId,
                ["SubscriptionId"] = user.StripeSubscriptionId
            });

            if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
            {
                skippedNoSubscription++;

                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "reconcile_user",
                    "no_action_needed",
                    "missing_subscription_id");
                continue;
            }

            var dependencyWatch = Stopwatch.StartNew();
            var sub = await _stripeAdminClient.GetSubscriptionAsync(user.StripeSubscriptionId!, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
                "dependency",
                "stripe",
                "get_subscription",
                "Stripe API",
                dependencyWatch.ElapsedMilliseconds,
                true,
                sub is not null);

            if (sub is null)
            {
                skippedMissingStripe++;

                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "reconcile_user",
                    "no_action_needed",
                    "stripe_subscription_not_found");
                continue;
            }

            // Guard against stale Stripe reads regressing a terminal local projection.
            // A deleted subscription should not flip back to active on the same subscription id.
            if (string.Equals(user.SubscriptionStatus, "deleted", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sub.Status, "deleted", StringComparison.OrdinalIgnoreCase)
                && string.Equals(user.StripeSubscriptionId, sub.SubscriptionId, StringComparison.OrdinalIgnoreCase))
            {
                skippedStale++;

                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} LocalStatus={LocalStatus} StripeStatus={StripeStatus}",
                    "decision",
                    "reconcile_user",
                    "skipped_out_of_order",
                    "local_deleted_state_preserved",
                    user.SubscriptionStatus,
                    sub.Status);

                continue;
            }

            var facts = new StripeSubscriptionFacts(
                CustomerId: sub.CustomerId,
                SubscriptionId: sub.SubscriptionId,
                Status: sub.Status,
                PriceId: sub.PriceId,
                PlanTerm: sub.Interval is "month" ? "monthly" : sub.Interval is "year" ? "annual" : null,
                CancelAtPeriodEnd: sub.CancelAtPeriodEnd,
                CurrentPeriodEndUtc: sub.CurrentPeriodEndUtc,
                CanceledAtUtc: sub.CanceledAtUtc,
                EndedAtUtc: sub.EndedAtUtc
            );

            user.StripeCustomerId = sub.CustomerId;
            user.StripeSubscriptionId = sub.SubscriptionId;
            user.SubscriptionStatus = sub.Status;
            user.StripePriceId = sub.PriceId;
            user.IndividualPlanTerm = sub.Interval is "month" ? "monthly" : sub.Interval is "year" ? "annual" : user.IndividualPlanTerm;
            user.StripeCancelAtPeriodEnd = sub.CancelAtPeriodEnd;
            user.StripeCurrentPeriodEndUtc = sub.CurrentPeriodEndUtc;
            user.UpdatedAtUtc = nowUtc;

            var reduced = SubscriptionReducer.Reduce(
                facts,
                nowUtc,
                _individualGracePolicy,
                user.IndividualGraceEndsAtUtc);

            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} State={State} Reason={Reason} GraceEndsAtUtc={GraceEndsAtUtc}",
                "decision",
                "subscription_reduce",
                reduced.State,
                reduced.Reason,
                reduced.GraceEndsAtUtc);

            user.IndividualGraceEndsAtUtc = reduced.State == IndividualEntitlementState.Grace
                ? reduced.GraceEndsAtUtc
                : null;

            var applyWatch = Stopwatch.StartNew();
            await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

            _logger.LogDebug(
                "Step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} DurationMs={DurationMs}",
                "step",
                "access_recompute",
                "applied",
                applyWatch.ElapsedMilliseconds);

            reconciled++;
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Scanned={Scanned} Reconciled={Reconciled} SkippedNoSubscription={SkippedNoSubscription} SkippedMissingStripe={SkippedMissingStripe} SkippedStale={SkippedStale} DurationMs={DurationMs}",
            "outcome",
            "completed",
            scanned,
            reconciled,
            skippedNoSubscription,
            skippedMissingStripe,
            skippedStale,
            opWatch.ElapsedMilliseconds);
    }
}

