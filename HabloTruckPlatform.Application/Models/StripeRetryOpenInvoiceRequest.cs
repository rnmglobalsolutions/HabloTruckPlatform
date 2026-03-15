namespace HabloTruckPlatform.Application.Models;

public sealed class StripeRetryOpenInvoiceRequest
{
    public string ActorUserPk { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public string? SubscriptionId { get; set; }
}
