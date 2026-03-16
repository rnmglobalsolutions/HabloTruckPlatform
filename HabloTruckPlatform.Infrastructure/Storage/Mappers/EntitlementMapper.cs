using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage.Mappers;

public static class EntitlementMapper
{
    public static string Pk(string companyId) => $"{TablePrefixes.Entitlement}_{companyId}";

    public static EntitlementEntity ToEntity(Entitlement e)
        => new()
        {
            PartitionKey = Pk(e.CompanyId),
            RowKey = e.EntitlementId,

            CompanyId = e.CompanyId,
            EntitlementId = e.EntitlementId,
            SeatsTotal = e.SeatsTotal,
            SeatsUsed = e.SeatsUsed,
            IsOverCapacity = e.IsOverCapacity,
            Status = e.Status ?? "active",
            StartUtc = e.StartUtc,
            EndUtc = e.EndUtc
        };

    public static Entitlement FromEntity(EntitlementEntity e)
        => new()
        {
            CompanyId = e.CompanyId,
            EntitlementId = e.EntitlementId,
            SeatsTotal = e.SeatsTotal,
            SeatsUsed = e.SeatsUsed,
            IsOverCapacity = e.IsOverCapacity,
            Status = e.Status,
            StartUtc = e.StartUtc,
            EndUtc = e.EndUtc
        };
}
