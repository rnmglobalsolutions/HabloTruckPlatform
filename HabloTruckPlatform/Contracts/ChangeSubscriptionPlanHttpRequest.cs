namespace HabloTruckPlatform.Functions.Contracts;

public sealed class ChangeSubscriptionPlanHttpRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? EmailNormalized { get; set; }
    public string? Email { get; set; }
    public string? PhoneE164 { get; set; }
    public string? SubscriptionId { get; set; }
    public string? TargetPlanType { get; set; }
    public string? EffectiveWhen { get; set; }
}
