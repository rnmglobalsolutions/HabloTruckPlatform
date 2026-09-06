using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IStripeSubscriptionGateway
{
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default);

    Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(
        string subscriptionId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task<StripeSubscriptionSnapshot?> ChangeSubscriptionPriceAsync(
        string subscriptionId,
        string targetPriceId,
        string prorationBehavior,
        string? billingCycleAnchor,
        string idempotencyKey,
        CancellationToken ct = default)
        => throw new NotSupportedException("Subscription price changes are not supported by this gateway.");

    Task<StripeSubscriptionSnapshot?> ScheduleSubscriptionPriceChangeAtPeriodEndAsync(
        string subscriptionId,
        string targetPriceId,
        string idempotencyKey,
        CancellationToken ct = default)
        => throw new NotSupportedException("Scheduled subscription price changes are not supported by this gateway.");

    Task<StripeSubscriptionSnapshot?> UpdateSubscriptionQuantityAsync(
        string subscriptionId,
        int targetQuantity,
        string prorationBehavior,
        string idempotencyKey,
        CancellationToken ct = default)
        => throw new NotSupportedException("Subscription quantity changes are not supported by this gateway.");
}
