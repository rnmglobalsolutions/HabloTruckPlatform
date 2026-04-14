namespace HabloTruckPlatform.Application.Models;

public sealed class ChangeSubscriptionPlanRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? TargetPlanType { get; set; }
    public string? EffectiveWhen { get; set; }
}

