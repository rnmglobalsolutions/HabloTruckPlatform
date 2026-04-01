namespace HabloTruckPlatform.Application.Models;

public sealed class CompanyAdminInviteLookupRequest
{
    public string? CompanyId { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? EntitlementId { get; set; }
}
