namespace HabloTruckPlatform.Functions.Contracts;

public sealed class GetActiveInviteHttpRequest
{
    public string? CompanyId { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? EntitlementId { get; set; }
}
