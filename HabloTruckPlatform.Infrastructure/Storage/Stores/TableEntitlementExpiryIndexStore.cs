using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableEntitlementExpiryIndexStore : IEntitlementExpiryIndexStore
{
    private readonly TableClient _idx;
    private readonly ILogger<TableEntitlementExpiryIndexStore> _logger;

    public TableEntitlementExpiryIndexStore(TableServiceClient serviceClient, ILogger<TableEntitlementExpiryIndexStore>? logger = null)
    {
        _idx = serviceClient.GetTableClient(TableNames.EntitlementExpiryIndex);
        _logger = logger ?? NullLogger<TableEntitlementExpiryIndexStore>.Instance;
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

        var watch = Stopwatch.StartNew();
        await _idx.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs}",
            "persistence",
            "entitlement_expiry_index.upsert",
            _idx.Name,
            pk,
            rk,
            watch.ElapsedMilliseconds);
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

        var watch = Stopwatch.StartNew();
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

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} DurationMs={DurationMs} Count={Count}",
            "persistence",
            "entitlement_expiry_index.query_expiring",
            _idx.Name,
            expiryPk,
            watch.ElapsedMilliseconds,
            results.Count);

        return results;
    }

    public async Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pk) || string.IsNullOrWhiteSpace(rk))
            return;

        var watch = Stopwatch.StartNew();

        try
        {
            await _idx.DeleteEntityAsync(pk, rk, ETag.All, ct);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "entitlement_expiry_index.delete",
                _idx.Name,
                pk,
                rk,
                watch.ElapsedMilliseconds,
                true);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // already deleted
            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "entitlement_expiry_index.delete",
                _idx.Name,
                pk,
                rk,
                watch.ElapsedMilliseconds,
                false);
        }
    }
}
