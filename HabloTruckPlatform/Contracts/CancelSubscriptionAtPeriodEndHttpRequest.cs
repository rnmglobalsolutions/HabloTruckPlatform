namespace HabloTruckPlatform.Functions.Contracts;

public sealed class CancelSubscriptionAtPeriodEndHttpRequest
{
    // individual | company | fleet
    public string? Scope { get; set; }

    public string? ActorUserPk { get; set; }
    public string? ActorUserId { get; set; }
    public string? ManyChatSubscriberId { get; set; }

    public string? CompanyId { get; set; }
    public string? SubscriptionId { get; set; }
}
