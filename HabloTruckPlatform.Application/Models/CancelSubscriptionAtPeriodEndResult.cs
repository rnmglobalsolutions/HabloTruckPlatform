namespace HabloTruckPlatform.Application.Models;

public sealed class CancelSubscriptionAtPeriodEndResult
{
    public bool Result { get; set; }

    public string Scope { get; set; } = "individual";
    public string? SubscriptionId { get; set; }

    public bool CancelAtPeriodEnd { get; set; }
    public bool AlreadyScheduled { get; set; }

    public DateTimeOffset? EffectivePeriodEndUtc { get; set; }
    public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
    public DateTimeOffset? CanceledAtUtc { get; set; }
    public DateTimeOffset CancelRequestedAtUtc { get; set; }

    // Error code when Result == false
    public string? Error { get; set; }
}
