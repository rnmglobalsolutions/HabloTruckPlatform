namespace HabloTruckPlatform.Application.Models;

public sealed class ChangeSubscriptionPlanResult
{
    public bool Result { get; set; }
    public string? Error { get; set; }
    public string? SubscriptionId { get; set; }
    public string? PreviousPlanType { get; set; }
    public string? TargetPlanType { get; set; }
    public string? EffectiveWhen { get; set; }
    public string? StripePriceId { get; set; }
    public string? Interval { get; set; }
    public string? SubscriptionStatus { get; set; }
    public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
}

