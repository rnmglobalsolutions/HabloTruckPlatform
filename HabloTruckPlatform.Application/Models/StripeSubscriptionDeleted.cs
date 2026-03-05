namespace HabloTruckPlatform.Application.Models;

public sealed record StripeSubscriptionDeleted(
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId,
    string StripeSubscriptionId,
    string? PriceId,
    string? Interval,
    bool? CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc);