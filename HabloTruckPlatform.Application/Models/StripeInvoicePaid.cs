namespace HabloTruckPlatform.Application.Models;

public sealed record StripeInvoicePaid(
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId);