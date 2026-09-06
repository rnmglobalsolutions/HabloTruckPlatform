using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IEntitlementExpiryIndexStore
{
    Task EnsureTableAsync(CancellationToken ct = default);

    Task UpsertAsync(EntitlementRef entitlementRef, DateTimeOffset endUtc, CancellationToken ct = default);

    Task<IReadOnlyList<EntitlementExpiryIndexItem>> QueryExpiringAsync(
        string expiryPk,
        DateTimeOffset nowUtc,
        int take = 500,
        CancellationToken ct = default);

    // ✅ Add this
    Task DeleteAsync(string pk, string rk, CancellationToken ct = default);
}