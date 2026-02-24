using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IEntitlementExpiryIndexStore
{
    Task UpsertAsync(EntitlementRef entitlementRef, DateTimeOffset endUtc, CancellationToken ct = default);

    Task<IReadOnlyList<EntitlementExpiryIndexItem>> QueryExpiringAsync(string expiryPk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default);
}