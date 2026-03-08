namespace HabloTruckPlatform.Application.Models;

public sealed record StripeSubscriptionSnapshot(
    string SubscriptionId,
    string CustomerId,
    string Status,
    string? PriceId,
    string? Interval,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc
);