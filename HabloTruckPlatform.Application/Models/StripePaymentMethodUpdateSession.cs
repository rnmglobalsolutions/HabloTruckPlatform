namespace HabloTruckPlatform.Application.Models;

public sealed record StripePaymentMethodUpdateSession(
    string SessionId,
    string CustomerId,
    string? SubscriptionId,
    string Url);
