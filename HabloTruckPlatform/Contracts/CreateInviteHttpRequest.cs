namespace HabloTruckPlatform.Functions.Contracts;

public sealed class CreateInviteHttpRequest
{
    public string? CompanyId { get; set; }
    public string? EntitlementId { get; set; }

    public int? MaxUses { get; set; }           // default 25
    public int? ExpiresInDays { get; set; }     // optional
    public string? CreatedBy { get; set; }      // optional
    public string? Code { get; set; }           // optional override
}