namespace HabloTruckPlatform.Application.Models;

public sealed record EntitlementExpiryIndexItem(
    string Pk,
    string Rk,
    string CompanyId,
    string EntitlementId,
    DateTimeOffset EndUtc);