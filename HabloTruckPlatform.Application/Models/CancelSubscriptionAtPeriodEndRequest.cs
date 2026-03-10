namespace HabloTruckPlatform.Application.Models;

public sealed class CancelSubscriptionAtPeriodEndRequest
{
    // individual | company | fleet
    public string Scope { get; set; } = "individual";

    // Required actor context (used for authorization checks)
    public string ActorUserPk { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;

    // company/fleet scope
    public string? CompanyId { get; set; }

    // Optional override; if omitted, use case resolves from context
    public string? SubscriptionId { get; set; }
}
