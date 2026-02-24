using HabloTruckPlatform.Domain.Models;

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
}