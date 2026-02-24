using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface ISeatAssignmentStore
{
    Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Create or update seat assignment for a user (idempotent).
    /// </summary>
    Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default);

    Task RevokeAsync(string companyId, string userId, CancellationToken ct = default);

    Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default);
}