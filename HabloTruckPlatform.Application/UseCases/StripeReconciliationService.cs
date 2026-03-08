using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class StripeReconciliationService
{
    private readonly IUserStore _userStore;
    private readonly IStripeAdminClient _stripeAdminClient;
    private readonly IGraceIndexStore _graceIndexStore;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly IClock _clock;
    private readonly GracePolicy _individualGracePolicy;

    public StripeReconciliationService(
        IUserStore userStore,
        IStripeAdminClient stripeAdminClient,
        IGraceIndexStore graceIndexStore,
        AccessOrchestrator accessOrchestrator,
        IClock clock,
        GracePolicy individualGracePolicy)
    {
        _userStore = userStore;
        _stripeAdminClient = stripeAdminClient;
        _graceIndexStore = graceIndexStore;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
        _individualGracePolicy = individualGracePolicy;
    }

    public async Task RunAsync(int take = 500, CancellationToken ct = default)
    {
        var users = await _userStore.QueryUsersWithStripeAsync(take, ct);
        var nowUtc = _clock.UtcNow;

        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
                continue;

            var sub = await _stripeAdminClient.GetSubscriptionAsync(user.StripeSubscriptionId!, ct);
            if (sub is null)
                continue;

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

            user.IndividualGraceEndsAtUtc = reduced.State == IndividualEntitlementState.Grace
                ? reduced.GraceEndsAtUtc
                : null;

            await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);
        }
    }
}