using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
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

    public async Task<IReadOnlyList<Entitlement>> QueryForRecountAsync(int take = int.MaxValue, CancellationToken ct = default)
    {
        if (take <= 0)
            return Array.Empty<Entitlement>();

        var results = new List<Entitlement>(take is > 0 and < 1024 ? take : 1024);

        await foreach (var entity in EntitlementsTable.QueryAsync<EntitlementEntity>(
                           maxPerPage: 1000,
                           cancellationToken: ct))
        {
            results.Add(EntitlementMapper.FromEntity(entity));
            if (results.Count >= take)
                break;
        }

        return results;
    }

    public async Task<SeatReservationResult> TryReserveSeatAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return new SeatReservationResult(SeatReservationOutcome.NotFound, null, "entitlement_not_found");

        var pk = EntitlementMapper.Pk(companyId.Trim());
        var rk = entitlementId.Trim();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var entity = await _repo.GetOrNullAsync<EntitlementEntity>(EntitlementsTable, pk, rk, ct);
            if (entity is null)
                return new SeatReservationResult(SeatReservationOutcome.NotFound, null, "entitlement_not_found");

            var nowUtc = DateTimeOffset.UtcNow;
            var isActive = string.Equals(entity.Status, "active", StringComparison.OrdinalIgnoreCase)
                           && (entity.EndUtc is null || entity.EndUtc > nowUtc);

            if (!isActive)
                return new SeatReservationResult(SeatReservationOutcome.Inactive, EntitlementMapper.FromEntity(entity), "entitlement_not_active");

            entity.SeatsTotal = Math.Max(entity.SeatsTotal, 0);
            entity.SeatsUsed = Math.Max(entity.SeatsUsed, 0);
            entity.IsOverCapacity = entity.SeatsUsed > entity.SeatsTotal;

            if (entity.IsOverCapacity)
            {
                try
                {
                    await EntitlementsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                    return new SeatReservationResult(SeatReservationOutcome.OverCapacity, EntitlementMapper.FromEntity(entity), "company_over_capacity");
                }
                catch (RequestFailedException ex) when (ex.Status is 412 or 409)
                {
                    continue;
                }
            }

            if (entity.SeatsUsed >= entity.SeatsTotal)
                return new SeatReservationResult(SeatReservationOutcome.NoCapacity, EntitlementMapper.FromEntity(entity), "no_seats_available");

            entity.SeatsUsed += 1;
            entity.IsOverCapacity = entity.SeatsUsed > entity.SeatsTotal;

            try
            {
                await EntitlementsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return new SeatReservationResult(SeatReservationOutcome.Reserved, EntitlementMapper.FromEntity(entity));
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 409)
            {
                // optimistic concurrency conflict -> retry
            }
        }

        return new SeatReservationResult(SeatReservationOutcome.NoCapacity, null, "seat_reservation_conflict");
    }

    public async Task<Entitlement?> SyncSeatsUsedAsync(string companyId, string entitlementId, int seatsUsedFloor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return null;

        var pk = EntitlementMapper.Pk(companyId.Trim());
        var rk = entitlementId.Trim();
        var normalizedFloor = Math.Max(seatsUsedFloor, 0);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var entity = await _repo.GetOrNullAsync<EntitlementEntity>(EntitlementsTable, pk, rk, ct);
            if (entity is null)
                return null;

            var nextSeatsUsed = Math.Max(Math.Max(entity.SeatsUsed, 0), normalizedFloor);
            var nextIsOverCapacity = nextSeatsUsed > Math.Max(entity.SeatsTotal, 0);

            if (nextSeatsUsed == entity.SeatsUsed && nextIsOverCapacity == entity.IsOverCapacity)
                return EntitlementMapper.FromEntity(entity);

            entity.SeatsUsed = nextSeatsUsed;
            entity.IsOverCapacity = nextIsOverCapacity;

            try
            {
                await EntitlementsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return EntitlementMapper.FromEntity(entity);
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 409)
            {
                // optimistic concurrency conflict -> retry
            }
        }

        return await GetAsync(companyId, entitlementId, ct);
    }

    public async Task ReleaseSeatReservationAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return;

        var pk = EntitlementMapper.Pk(companyId.Trim());
        var rk = entitlementId.Trim();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var entity = await _repo.GetOrNullAsync<EntitlementEntity>(EntitlementsTable, pk, rk, ct);
            if (entity is null)
                return;

            entity.SeatsTotal = Math.Max(entity.SeatsTotal, 0);
            entity.SeatsUsed = Math.Max(entity.SeatsUsed, 0);

            if (entity.SeatsUsed > 0)
                entity.SeatsUsed -= 1;

            entity.IsOverCapacity = entity.SeatsUsed > entity.SeatsTotal;

            try
            {
                await EntitlementsTable.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 409)
            {
                // optimistic concurrency conflict -> retry
            }
        }
    }
}
