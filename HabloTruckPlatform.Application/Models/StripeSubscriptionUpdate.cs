namespace HabloTruckPlatform.Application.Models;

public sealed record StripeSubscriptionUpdate(
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId,
    string StripeSubscriptionId,
    string SubscriptionStatus,
    string? PriceId,
    string? Interval,
    bool? CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc);