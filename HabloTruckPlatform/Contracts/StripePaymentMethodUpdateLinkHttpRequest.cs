namespace HabloTruckPlatform.Functions.Contracts;

public sealed class StripePaymentMethodUpdateLinkHttpRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ReturnUrl { get; set; }
}
