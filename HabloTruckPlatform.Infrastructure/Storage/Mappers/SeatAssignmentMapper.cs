using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage.Mappers;

public static class SeatAssignmentMapper
{
    public static string Pk(string companyId) => $"{TablePrefixes.SeatAssignment}_{companyId}";

    public static SeatAssignmentEntity ToEntity(SeatAssignment s)
        => new()
        {
            PartitionKey = Pk(s.CompanyId),
            RowKey = s.UserId,

            CompanyId = s.CompanyId,
            UserId = s.UserId,
            EntitlementId = s.EntitlementId,
            Status = s.Status ?? "active",
            AssignedAtUtc = s.AssignedAtUtc,
            UpdatedAtUtc = s.UpdatedAtUtc,
            RevokedAtUtc = s.RevokedAtUtc
        };

    public static SeatAssignment FromEntity(SeatAssignmentEntity e)
        => new()
        {
            CompanyId = e.CompanyId,
            UserId = e.UserId,
            EntitlementId = e.EntitlementId,
            Status = e.Status,
            AssignedAtUtc = e.AssignedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc,
            RevokedAtUtc = e.RevokedAtUtc
        };
}
