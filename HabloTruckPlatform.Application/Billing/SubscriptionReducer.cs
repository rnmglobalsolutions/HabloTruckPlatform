using HabloTruckPlatform.Domain.Access;

namespace HabloTruckPlatform.Application.Billing;

public static class SubscriptionReducer
{
    public static IndividualEntitlementResult Reduce(
        StripeSubscriptionFacts f,
        DateTimeOffset nowUtc,
        GracePolicy gracePolicy,
        DateTimeOffset? existingGraceEndsAtUtc)
    {
        // “Paid-through” rules everything: prevents proration/retry/upgrade false grace
        var paidThrough = f.CurrentPeriodEndUtc is not null && f.CurrentPeriodEndUtc > nowUtc;

        var status = (f.Status ?? "").Trim().ToLowerInvariant();
        var activeLike = status is "active" or "trialing";
        var delinquent = status is "past_due" or "unpaid" or "incomplete" or "incomplete_expired";

        // Ended = Stripe says it ended and time has passed
        var ended = f.EndedAtUtc is not null && f.EndedAtUtc <= nowUtc;

        // Cancel-at-period-end: until period end, user is still paid-through (handled above)
        // After period end, Stripe usually flips status to canceled/ended.
        // If status hasn’t flipped yet, we still rely on paidThrough and ended.

        // 1) If paid through and not ended -> allow (no grace)
        if (paidThrough && !ended)
            return new IndividualEntitlementResult(IndividualEntitlementState.PaidThrough, null, "paid_through");

        // 2) Active-like and not ended -> allow (no grace)
        if (activeLike && !ended)
            return new IndividualEntitlementResult(IndividualEntitlementState.Active, null, "active_like");

        // 3) Delinquent and not paid-through -> grace
        if (delinquent && !paidThrough && !ended)
            return new IndividualEntitlementResult(
                IndividualEntitlementState.Grace,
                ExtendOrStartGrace(nowUtc, gracePolicy, existingGraceEndsAtUtc),
                "delinquent_grace");

        // 4) Canceled/deleted/ended -> grace (then block after grace expires via sweeper / recompute)
        if (status is "canceled" or "deleted" || ended)
            return new IndividualEntitlementResult(
                IndividualEntitlementState.Grace,
                ExtendOrStartGrace(nowUtc, gracePolicy, existingGraceEndsAtUtc),
                "canceled_or_ended_grace");

        // 5) Otherwise blocked
        return new IndividualEntitlementResult(IndividualEntitlementState.Blocked, null, "blocked_default");
    }

    private static DateTimeOffset ExtendOrStartGrace(
        DateTimeOffset nowUtc,
        GracePolicy gracePolicy,
        DateTimeOffset? existingGraceEndsAtUtc)
    {
        // IMPORTANT: don’t “restart grace” on every retry event
        // Keep existing grace if it’s still in the future.
        if (existingGraceEndsAtUtc is not null && existingGraceEndsAtUtc > nowUtc)
            return existingGraceEndsAtUtc.Value;

        return nowUtc.Add(gracePolicy.Duration);
    }
}

public sealed record IndividualEntitlementResult(
    IndividualEntitlementState State,
    DateTimeOffset? GraceEndsAtUtc,
    string Reason);

public sealed record StripeSubscriptionFacts(
    string? CustomerId,
    string? SubscriptionId,
    string? Status,
    string? PriceId,
    string? PlanTerm, // "monthly" | "annual" | null (derived in App layer)
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc);

public enum IndividualEntitlementState
{
    None = 0,
    Active = 1,       // active/trialing, not ended
    PaidThrough = 2,  // current_period_end in the future
    Grace = 3,
    Blocked = 4
}