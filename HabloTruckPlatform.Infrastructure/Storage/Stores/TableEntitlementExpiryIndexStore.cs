using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableEntitlementExpiryIndexStore : IEntitlementExpiryIndexStore
{
    private readonly TableClient _idx;

    public TableEntitlementExpiryIndexStore(TableServiceClient serviceClient)
    {
        _idx = serviceClient.GetTableClient(TableNames.EntitlementExpiryIndex);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _idx.CreateIfNotExistsAsync(ct);

    public async Task UpsertAsync(EntitlementRef entitlementRef, DateTimeOffset endUtc, CancellationToken ct = default)
    {
        var pk = $"{TablePrefixes.EntitlementExpiry}_{endUtc:yyyyMMdd}";
        var rk = $"{endUtc.Ticks:D19}_{entitlementRef.CompanyId}_{entitlementRef.EntitlementId}";

        var entity = new EntitlementExpiryIndexEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            CompanyId = entitlementRef.CompanyId,
            EntitlementId = entitlementRef.EntitlementId,
            EndUtc = endUtc
        };

        await _idx.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<EntitlementExpiryIndexItem>> QueryExpiringAsync(
        string expiryPk,
        DateTimeOffset nowUtc,
        int take = 500,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expiryPk))
            throw new ArgumentException("expiryPk is required.", nameof(expiryPk));

        if (take <= 0) take = 500;

        var maxRk = $"{nowUtc.Ticks:D19}_~~~~";
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {expiryPk} and RowKey le {maxRk}");

        var results = new List<EntitlementExpiryIndexItem>(Math.Min(take, 500));

        await foreach (var e in _idx.QueryAsync<EntitlementExpiryIndexEntity>(
                           filter: filter,
                           maxPerPage: take,
                           cancellationToken: ct))
        {
            results.Add(new EntitlementExpiryIndexItem(
                e.PartitionKey,
                e.RowKey,
                e.CompanyId,
                e.EntitlementId,
                e.EndUtc));

            if (results.Count >= take) break;
        }

        return results;
    }

    public async Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pk) || string.IsNullOrWhiteSpace(rk))
            return;

        try
        {
            await _idx.DeleteEntityAsync(pk, rk, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // already deleted
        }
    }
}