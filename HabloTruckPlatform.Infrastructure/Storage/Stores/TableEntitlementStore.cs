using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableEntitlementStore : IEntitlementStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableEntitlementStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient EntitlementsTable => _factory.GetClient(TableNames.Entitlements);

    public Task EnsureTableAsync(CancellationToken ct = default)
        => _factory.EnsureTableAsync(TableNames.Entitlements, ct);

    public async Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return null;

        var pk = EntitlementMapper.Pk(companyId.Trim());
        var rk = entitlementId.Trim();

        var entity = await _repo.GetOrNullAsync<EntitlementEntity>(EntitlementsTable, pk, rk, ct);
        return entity is null ? null : EntitlementMapper.FromEntity(entity);
    }

    public async Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
    {
        var entity = EntitlementMapper.ToEntity(entitlement);

        // Insert-only semantics (fail if exists)
        try
        {
            var ok = await _repo.TryInsertAsync(EntitlementsTable, entity, ct);
            if (!ok)
                throw new InvalidOperationException($"Entitlement already exists. PK={entity.PartitionKey}, RK={entity.RowKey}");
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // In case your repo doesn't catch 409 for some reason
            throw new InvalidOperationException($"Entitlement already exists. PK={entity.PartitionKey}, RK={entity.RowKey}", ex);
        }
    }

    public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
    {
        var entity = EntitlementMapper.ToEntity(entitlement);
        return _repo.UpsertAsync(EntitlementsTable, entity, TableUpdateMode.Replace, ct);
    }

    public async Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
    {
        var ent = await GetAsync(companyId, entitlementId, ct);
        if (ent is null) return;

        ent.Status = status;
        await UpsertAsync(ent, ct);
    }
}