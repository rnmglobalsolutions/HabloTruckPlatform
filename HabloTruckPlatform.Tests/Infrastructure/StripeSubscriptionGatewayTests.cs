using System.Reflection;
using HabloTruckPlatform.Infrastructure.Stripe;
using Stripe;

namespace HabloTruckPlatform.Domain.Tests.Infrastructure;

public sealed class StripeSubscriptionGatewayTests
{
    [Fact]
    public void ComputeEffectivePeriodEndUtc_Should_ReturnLatestItemPeriodEnd_When_MultipleItemsExist()
    {
        // Arrange
        var item1End = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var item2End = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        var items = new[]
        {
            NewSubscriptionItem(item1End),
            NewSubscriptionItem(item2End)
        };

        // Act
        var effectiveEnd = StripeSubscriptionGateway.ComputeEffectivePeriodEndUtc(items);

        // Assert
        Assert.Equal(new DateTimeOffset(item2End, TimeSpan.Zero), effectiveEnd);
    }

    [Fact]
    public void MapSnapshot_Should_PreserveCanceledAtAndUseItemPeriodEnd_When_SubscriptionHasValues()
    {
        // Arrange
        var canceledAt = new DateTime(2026, 3, 10, 12, 30, 0, DateTimeKind.Utc);
        var item1End = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var item2End = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);

        var sub = new Subscription();
        SetProperty(sub, "Id", "sub_period_end_1");
        SetProperty(sub, "CustomerId", "cus_period_end_1");
        SetProperty(sub, "Status", "active");
        SetProperty(sub, "CancelAtPeriodEnd", true);
        SetProperty(sub, "CanceledAt", canceledAt);

        var itemList = new StripeList<SubscriptionItem>();
        SetProperty(itemList, "Data", new List<SubscriptionItem>
        {
            NewSubscriptionItem(item1End),
            NewSubscriptionItem(item2End)
        });

        SetProperty(sub, "Items", itemList);

        // Act
        var snapshot = StripeSubscriptionGateway.MapSnapshot(sub);

        // Assert
        Assert.Equal("sub_period_end_1", snapshot.SubscriptionId);
        Assert.True(snapshot.CancelAtPeriodEnd);
        Assert.Equal(new DateTimeOffset(canceledAt, TimeSpan.Zero), snapshot.CanceledAtUtc);
        Assert.Equal(new DateTimeOffset(item2End, TimeSpan.Zero), snapshot.CurrentPeriodEndUtc);
    }

    [Fact]
    public void BuildPriceChangeOptions_Should_RequireCompletedPayment_When_ImmediateInvoiceIsCreated()
    {
        var options = StripeSubscriptionGateway.BuildPriceChangeOptions(
            "si_123",
            "price_yearly",
            "always_invoice",
            "now");

        Assert.Equal("error_if_incomplete", options.PaymentBehavior);
        Assert.Equal("always_invoice", options.ProrationBehavior);
        Assert.Equal(SubscriptionBillingCycleAnchor.Now, options.BillingCycleAnchor);
    }

    [Fact]
    public void BuildQuantityChangeOptions_Should_NotRequirePayment_When_NoProrationIsUsed()
    {
        var options = StripeSubscriptionGateway.BuildQuantityChangeOptions(
            "si_123",
            8,
            "none");

        Assert.Null(options.PaymentBehavior);
        Assert.Equal("none", options.ProrationBehavior);
    }

    [Fact]
    public void BuildScheduledPriceChangeOptions_Should_CreateCurrentAndFuturePhases()
    {
        var start = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var end = start.AddMonths(9);

        var options = StripeSubscriptionGateway.BuildScheduledPriceChangeOptions(
            "price_yearly",
            "price_monthly",
            1,
            start,
            end);

        Assert.Equal("release", options.EndBehavior);
        Assert.Equal("none", options.ProrationBehavior);
        Assert.Equal(2, options.Phases.Count);
        Assert.Equal("price_yearly", options.Phases[0].Items[0].Price);
        Assert.Equal("price_monthly", options.Phases[1].Items[0].Price);
        Assert.Equal(1, options.Phases[1].Items[0].Quantity);
        Assert.Equal("month", options.Phases[1].Duration!.Interval);
        Assert.Equal(1, options.Phases[1].Duration!.IntervalCount);
    }

    private static SubscriptionItem NewSubscriptionItem(DateTime currentPeriodEndUtc)
    {
        var item = new SubscriptionItem();
        SetProperty(item, "CurrentPeriodEnd", currentPeriodEndUtc);
        return item;
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var prop = target.GetType().GetProperty(propertyName, flags);
        if (prop is null)
            throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().FullName}.");

        if (prop.SetMethod is not null)
        {
            prop.SetValue(target, value);
            return;
        }

        var backing = target.GetType().GetField($"<{propertyName}>k__BackingField", flags);
        if (backing is null)
            throw new InvalidOperationException($"Property '{propertyName}' has no setter/backing field on {target.GetType().FullName}.");

        backing.SetValue(target, value);
    }
}
