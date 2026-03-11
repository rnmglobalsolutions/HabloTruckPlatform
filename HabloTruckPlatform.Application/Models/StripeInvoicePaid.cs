namespace HabloTruckPlatform.Application.Models;

public sealed record StripeInvoicePaid(
    string StripeEventId,
    string StripeSubscriptionId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId,
    string? PriceId,
    string? Interval,
    DateTimeOffset? CurrentPeriodEndUtc = null);
