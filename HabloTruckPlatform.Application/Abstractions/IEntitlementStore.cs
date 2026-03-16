using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IEntitlementStore
{
    Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default);

    Task CreateAsync(Entitlement entitlement, CancellationToken ct = default);

    Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default);

    /// <summary>
    /// Mark entitlement expired/refunded (implementation sets Status, audit, etc.).
    /// </summary>
    Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default);

    Task<SeatReservationResult> TryReserveSeatAsync(string companyId, string entitlementId, CancellationToken ct = default)
        => throw new NotSupportedException("Seat reservation is not supported by this entitlement store.");

    Task<Entitlement?> SyncSeatsUsedAsync(string companyId, string entitlementId, int seatsUsedFloor, CancellationToken ct = default)
        => throw new NotSupportedException("Seat usage sync is not supported by this entitlement store.");

    Task<IReadOnlyList<Entitlement>> QueryForRecountAsync(int take = int.MaxValue, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Entitlement>>(Array.Empty<Entitlement>());

    Task ReleaseSeatReservationAsync(string companyId, string entitlementId, CancellationToken ct = default)
        => throw new NotSupportedException("Seat reservation release is not supported by this entitlement store.");
}
