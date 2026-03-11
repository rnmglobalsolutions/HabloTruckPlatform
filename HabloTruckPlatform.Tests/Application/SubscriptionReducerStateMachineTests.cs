using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Domain.Access;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class SubscriptionReducerStateMachineTests
{
    private static readonly GracePolicy GracePolicy = new(72);

    [Fact]
    public void Reduce_Should_ReturnPaidThrough_When_PeriodEndIsInFuture()
    {
        var now = Utc(2026, 3, 10, 12);
        var result = SubscriptionReducer.Reduce(
            Facts(status: "active", periodEnd: now.AddDays(10)),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.PaidThrough, result.State);
        Assert.Null(result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_ReturnActive_When_ActiveLikeAndNotPaidThrough()
    {
        var now = Utc(2026, 3, 10, 12);
        var result = SubscriptionReducer.Reduce(
            Facts(status: "trialing", periodEnd: null),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.Active, result.State);
        Assert.Null(result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_StartGrace_When_DelinquentAndNotPaidThrough()
    {
        var now = Utc(2026, 3, 10, 12);
        var result = SubscriptionReducer.Reduce(
            Facts(status: "past_due", periodEnd: null),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.Grace, result.State);
        Assert.Equal(now.AddHours(72), result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_KeepExistingGrace_When_DelinquentRetryArrivesWithinGraceWindow()
    {
        var now = Utc(2026, 3, 10, 12);
        var existingGrace = now.AddHours(20);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "unpaid", periodEnd: null),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: existingGrace);

        Assert.Equal(IndividualEntitlementState.Grace, result.State);
        Assert.Equal(existingGrace, result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_OpenFreshGrace_When_DelinquentRetryArrivesAfterPreviousGraceExpired()
    {
        var now = Utc(2026, 3, 10, 12);
        var expiredGrace = now.AddHours(-1);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "incomplete", periodEnd: null),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: expiredGrace);

        Assert.Equal(IndividualEntitlementState.Grace, result.State);
        Assert.Equal(now.AddHours(72), result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_ReturnPaidThrough_When_CancelAtPeriodEndAndCurrentPeriodStillFuture()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "active", cancelAtPeriodEnd: true, periodEnd: now.AddDays(5)),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: now.AddHours(2));

        Assert.Equal(IndividualEntitlementState.PaidThrough, result.State);
        Assert.Null(result.GraceEndsAtUtc);
        Assert.Equal("cancel_scheduled_paid_through", result.Reason);
    }

    [Fact]
    public void Reduce_Should_ReturnGrace_When_CancelAtPeriodEndAndPeriodPassedWithCanceledStatus()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(
                status: "canceled",
                cancelAtPeriodEnd: true,
                periodEnd: now.AddHours(-1),
                endedAt: now.AddHours(-1)),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.Grace, result.State);
        Assert.Equal(now.AddHours(72), result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_ReturnBlocked_When_SubscriptionDeleted()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "deleted", endedAt: now),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: now.AddHours(10));

        Assert.Equal(IndividualEntitlementState.Blocked, result.State);
        Assert.Null(result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_TransitionFromPaymentFailedToRecoveredPaidThrough_When_InvoicePaidSequenceApplied()
    {
        var now = Utc(2026, 3, 10, 12);

        var failed = SubscriptionReducer.Reduce(
            Facts(status: "past_due", periodEnd: null),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        var recovered = SubscriptionReducer.Reduce(
            Facts(status: "active", periodEnd: now.AddDays(30)),
            now.AddMinutes(1),
            GracePolicy,
            existingGraceEndsAtUtc: failed.GraceEndsAtUtc);

        Assert.Equal(IndividualEntitlementState.Grace, failed.State);
        Assert.Equal(IndividualEntitlementState.PaidThrough, recovered.State);
        Assert.Null(recovered.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_BeDeterministic_ForSameInput()
    {
        var now = Utc(2026, 3, 10, 12);
        var facts = Facts(
            status: "active",
            cancelAtPeriodEnd: true,
            periodEnd: now.AddDays(7),
            endedAt: null,
            canceledAt: now.AddMinutes(-10));

        var first = SubscriptionReducer.Reduce(facts, now, GracePolicy, existingGraceEndsAtUtc: null);
        var second = SubscriptionReducer.Reduce(facts, now, GracePolicy, existingGraceEndsAtUtc: null);

        Assert.Equal(first, second);
    }


    [Fact]
    public void Reduce_Should_ReturnGrace_When_StatusCanceledAndNotPaidThrough()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "canceled", periodEnd: now.AddDays(-1)),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.Grace, result.State);
        Assert.Equal(now.AddHours(72), result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_ReturnBlocked_ForUnknownStatusWithoutPaidThrough()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "paused", periodEnd: null),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.Blocked, result.State);
        Assert.Null(result.GraceEndsAtUtc);
        Assert.Equal("blocked_default", result.Reason);
    }
    [Fact]
    public void Reduce_Should_KeepPaidThrough_When_StatusIsDelinquentButPeriodEndIsStillFuture()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "past_due", periodEnd: now.AddHours(6)),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: now.AddHours(2));

        Assert.Equal(IndividualEntitlementState.PaidThrough, result.State);
        Assert.Null(result.GraceEndsAtUtc);
        Assert.Equal("paid_through", result.Reason);
    }

    [Fact]
    public void Reduce_Should_KeepPaidThrough_When_StatusCanceledButCurrentPeriodHasNotEndedYet()
    {
        var now = Utc(2026, 3, 10, 12);

        var result = SubscriptionReducer.Reduce(
            Facts(status: "canceled", periodEnd: now.AddDays(2)),
            now,
            GracePolicy,
            existingGraceEndsAtUtc: null);

        Assert.Equal(IndividualEntitlementState.PaidThrough, result.State);
        Assert.Null(result.GraceEndsAtUtc);
        Assert.Equal("paid_through", result.Reason);
    }

    private static StripeSubscriptionFacts Facts(
        string status,
        bool cancelAtPeriodEnd = false,
        DateTimeOffset? periodEnd = null,
        DateTimeOffset? canceledAt = null,
        DateTimeOffset? endedAt = null)
        => new(
            CustomerId: "cus_test",
            SubscriptionId: "sub_test",
            Status: status,
            PriceId: "price_test",
            PlanTerm: "monthly",
            CancelAtPeriodEnd: cancelAtPeriodEnd,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: canceledAt,
            EndedAtUtc: endedAt);

    private static DateTimeOffset Utc(int year, int month, int day, int hour)
        => new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}


