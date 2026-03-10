using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Domain.Access;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class SubscriptionReducerCancelBehaviorTests
{
    [Fact]
    public void Reduce_Should_ReturnPaidThrough_When_CancelScheduledAndPeriodEndIsInFuture()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var facts = new StripeSubscriptionFacts(
            CustomerId: "cus_1",
            SubscriptionId: "sub_1",
            Status: "active",
            PriceId: "price_1",
            PlanTerm: "monthly",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: now.AddDays(10),
            CanceledAtUtc: null,
            EndedAtUtc: null);

        // Act
        var result = SubscriptionReducer.Reduce(facts, now, new GracePolicy(72), existingGraceEndsAtUtc: null);

        // Assert
        Assert.Equal(IndividualEntitlementState.PaidThrough, result.State);
        Assert.Null(result.GraceEndsAtUtc);
    }

    [Fact]
    public void Reduce_Should_ReturnBlocked_When_StatusIsDeleted()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var facts = new StripeSubscriptionFacts(
            CustomerId: "cus_2",
            SubscriptionId: "sub_2",
            Status: "deleted",
            PriceId: "price_2",
            PlanTerm: "monthly",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: null,
            CanceledAtUtc: now.AddMinutes(-1),
            EndedAtUtc: now.AddMinutes(-1));

        // Act
        var result = SubscriptionReducer.Reduce(facts, now, new GracePolicy(72), existingGraceEndsAtUtc: now.AddHours(12));

        // Assert
        Assert.Equal(IndividualEntitlementState.Blocked, result.State);
        Assert.Null(result.GraceEndsAtUtc);
    }
}
