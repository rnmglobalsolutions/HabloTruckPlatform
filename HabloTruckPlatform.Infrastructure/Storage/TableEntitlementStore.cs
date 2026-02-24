using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableEntitlementStore : IEntitlementStore
{
    private readonly TableClient _entitlements;

    public TableEntitlementStore(TableServiceClient serviceClient)
    {
        _entitlements = serviceClient.GetTableClient(TableNames.Entitlements);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _entitlements.CreateIfNotExistsAsync(ct);

    public async Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId)) return null;

        try
        {
            var pk = EntitlementMapper.Pk(companyId.Trim());
            var rk = entitlementId.Trim();

            var resp = await _entitlements.GetEntityAsync<EntitlementEntity>(pk, rk, cancellationToken: ct);
            return EntitlementMapper.FromEntity(resp.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
    {
        var entity = EntitlementMapper.ToEntity(entitlement);
        await _entitlements.AddEntityAsync(entity, ct);
    }

    public async Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
    {
        var entity = EntitlementMapper.ToEntity(entitlement);
        await _entitlements.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
    {
        var ent = await GetAsync(companyId, entitlementId, ct);
        if (ent is null) return;

        ent.Status = status;
        await UpsertAsync(ent, ct);
    }
}