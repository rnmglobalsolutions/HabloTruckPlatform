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

public sealed class TableSeatAssignmentStore : ISeatAssignmentStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableSeatAssignmentStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient SeatsTable => _factory.GetClient(TableNames.Seats);

    public Task EnsureTableAsync(CancellationToken ct = default)
        => _factory.EnsureTableAsync(TableNames.Seats, ct);

    public async Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(userId))
            return null;

        var pk = SeatAssignmentMapper.Pk(companyId.Trim());
        var rk = userId.Trim();

        var entity = await _repo.GetOrNullAsync<SeatAssignmentEntity>(SeatsTable, pk, rk, ct);
        return entity is null ? null : SeatAssignmentMapper.FromEntity(entity);
    }

    /// <summary>
    /// Create or update seat assignment (idempotent).
    /// </summary>
    public async Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (seat.AssignedAtUtc == default)
            seat.AssignedAtUtc = now;

        seat.UpdatedAtUtc = now;

        var entity = SeatAssignmentMapper.ToEntity(seat);
        await _repo.UpsertAsync(SeatsTable, entity, TableUpdateMode.Replace, ct);
    }

    public async Task<SeatActivationResult> EnsureActiveAsync(SeatAssignment seat, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var pk = SeatAssignmentMapper.Pk(seat.CompanyId.Trim());
        var rk = seat.UserId.Trim();

        if (seat.AssignedAtUtc == default)
            seat.AssignedAtUtc = now;

        seat.Status = "active";
        seat.UpdatedAtUtc = now;
        seat.RevokedAtUtc = null;

        var createEntity = SeatAssignmentMapper.ToEntity(seat);

        try
        {
            await SeatsTable.AddEntityAsync(createEntity, ct);
            return new SeatActivationResult(SeatActivationOutcome.Activated);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Fall through to optimistic update/read path.
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var existing = await _repo.GetOrNullAsync<SeatAssignmentEntity>(SeatsTable, pk, rk, ct);
            if (existing is null)
            {
                try
                {
                    await SeatsTable.AddEntityAsync(createEntity, ct);
                    return new SeatActivationResult(SeatActivationOutcome.Activated);
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    continue;
                }
            }

            if (string.Equals(existing.Status, "active", StringComparison.OrdinalIgnoreCase)
                && existing.RevokedAtUtc is null
                && string.Equals(existing.EntitlementId, seat.EntitlementId, StringComparison.OrdinalIgnoreCase))
            {
                return new SeatActivationResult(SeatActivationOutcome.AlreadyActive);
            }

            existing.CompanyId = seat.CompanyId;
            existing.UserId = seat.UserId;
            existing.EntitlementId = seat.EntitlementId;
            existing.Status = "active";
            existing.AssignedAtUtc = existing.AssignedAtUtc == default ? seat.AssignedAtUtc : existing.AssignedAtUtc;
            existing.UpdatedAtUtc = now;
            existing.RevokedAtUtc = null;

            try
            {
                await SeatsTable.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct);
                return new SeatActivationResult(SeatActivationOutcome.Activated);
            }
            catch (RequestFailedException ex) when (ex.Status is 412 or 409)
            {
                // Retry on optimistic concurrency conflict.
            }
        }

        return new SeatActivationResult(SeatActivationOutcome.Conflict, "seat_assignment_conflict");
    }

    public async Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
    {
        var seat = await GetAsync(companyId, userId, ct);
        if (seat is null) return;

        seat.Status = "revoked";
        seat.UpdatedAtUtc = DateTimeOffset.UtcNow;
        seat.RevokedAtUtc = seat.UpdatedAtUtc;

        await UpsertAsync(seat, ct);
    }

    public async Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return 0;

        var pk = SeatAssignmentMapper.Pk(companyId.Trim());
        var entId = entitlementId.Trim();

        // IMPORTANT: Use CreateQueryFilter so values are quoted/escaped correctly
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {pk} and Status eq {"active"} and EntitlementId eq {entId}");

        var count = 0;

        await foreach (var _ in SeatsTable.QueryAsync<SeatAssignmentEntity>(
                           filter: filter,
                           maxPerPage: 1000,
                           cancellationToken: ct))
        {
            count++;
        }

        return count;
    }
}
