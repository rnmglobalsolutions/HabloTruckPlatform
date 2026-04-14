namespace HabloTruckPlatform.Application.Models;

public sealed class UpdateCompanySeatQuantityRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? CompanyId { get; set; }
    public string? SubscriptionId { get; set; }
    public int TargetSeats { get; set; }
    public string? EffectiveWhen { get; set; }
}

