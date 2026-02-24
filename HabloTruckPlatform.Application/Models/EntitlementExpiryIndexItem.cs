namespace HabloTruckPlatform.Application.Models;

public sealed record EntitlementExpiryIndexItem(
    string CompanyId,
    string EntitlementId,
    DateTimeOffset EndUtc);