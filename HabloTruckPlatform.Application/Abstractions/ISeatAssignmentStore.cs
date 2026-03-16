using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface ISeatAssignmentStore
{
    Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Create or update seat assignment for a user (idempotent).
    /// </summary>
    Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default);

    Task<SeatActivationResult> EnsureActiveAsync(SeatAssignment seat, CancellationToken ct = default)
        => throw new NotSupportedException("Conditional seat activation is not supported by this seat assignment store.");

    Task RevokeAsync(string companyId, string userId, CancellationToken ct = default);

    Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default);
}
