namespace HabloTruckPlatform.Functions.Contracts;

public sealed class StripeRetryOpenInvoiceHttpRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? SubscriptionId { get; set; }
}
