namespace HabloTruckPlatform.Functions.Contracts;

public sealed class ListInvitesForCompanyHttpRequest
{
    public string? CompanyId { get; set; }
    public int? Take { get; set; }
}