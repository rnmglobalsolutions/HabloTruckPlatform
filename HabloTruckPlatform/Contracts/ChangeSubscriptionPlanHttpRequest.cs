namespace HabloTruckPlatform.Functions.Contracts;

public sealed class ChangeSubscriptionPlanHttpRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? TargetPlanType { get; set; }
    public string? EffectiveWhen { get; set; }
}

