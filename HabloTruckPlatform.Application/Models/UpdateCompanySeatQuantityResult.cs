namespace HabloTruckPlatform.Application.Models;

public sealed class UpdateCompanySeatQuantityResult
{
    public bool Result { get; set; }
    public string? Error { get; set; }
    public string? CompanyId { get; set; }
    public string? EntitlementId { get; set; }
    public string? SubscriptionId { get; set; }
    public int PreviousSeatsTotal { get; set; }
    public int TargetSeats { get; set; }
    public int SeatsUsed { get; set; }
    public bool IsOverCapacity { get; set; }
    public string? Direction { get; set; }
    public string? EffectiveWhen { get; set; }
    public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
}

