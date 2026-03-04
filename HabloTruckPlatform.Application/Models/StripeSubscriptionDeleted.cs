namespace HabloTruckPlatform.Application.Models;

public sealed record StripeSubscriptionDeleted(
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId,
    string StripeSubscriptionId,
    string? PriceId,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc);