namespace HabloTruckPlatform.Application.Models;

public sealed record StripeInvoicePaymentFailed(
    string StripeEventId,
    string StripeSubscriptionId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId,
    string? PriceId,
    string? Interval);