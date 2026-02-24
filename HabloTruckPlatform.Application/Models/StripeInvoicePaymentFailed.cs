namespace HabloTruckPlatform.Application.Models;

public sealed record StripeInvoicePaymentFailed(
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId);