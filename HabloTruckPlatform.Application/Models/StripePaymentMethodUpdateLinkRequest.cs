namespace HabloTruckPlatform.Application.Models;

public sealed class StripePaymentMethodUpdateLinkRequest
{
    public string ActorUserPk { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public string? SubscriptionId { get; set; }
    public string ReturnUrl { get; set; } = string.Empty;
}
