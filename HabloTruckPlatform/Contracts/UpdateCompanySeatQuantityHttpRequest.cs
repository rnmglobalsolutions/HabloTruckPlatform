namespace HabloTruckPlatform.Functions.Contracts;

public sealed class UpdateCompanySeatQuantityHttpRequest
{
    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? CompanyId { get; set; }
    public string? SubscriptionId { get; set; }
    public int TargetSeats { get; set; }
    public string? EffectiveWhen { get; set; }
}

