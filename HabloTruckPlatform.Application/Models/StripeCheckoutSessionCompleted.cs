namespace HabloTruckPlatform.Application.Models;

/// <summary>
/// Parsed data from Stripe checkout.session.completed (no Stripe SDK types here).
/// You build this DTO in the webhook Function.
/// </summary>
public sealed record StripeCheckoutSessionCompleted(
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string? StripeCustomerId,
    string CheckoutSessionId,
    string? PaymentIntentId,
    string CompanyId,
    string? CompanyName,
    string? AdminEmailNormalized,
    int SeatsTotal,
    string Term // "annual" or "lifetime"
);