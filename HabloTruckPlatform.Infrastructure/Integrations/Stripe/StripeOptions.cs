namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeOptions
{
    public required string WebhookSecret { get; init; }
}