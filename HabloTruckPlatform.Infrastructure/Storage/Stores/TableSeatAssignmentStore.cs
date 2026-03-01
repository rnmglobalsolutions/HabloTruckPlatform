using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
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

    public async Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
    {
        var seat = await GetAsync(companyId, userId, ct);
        if (seat is null) return;

        seat.Status = "revoked";
        seat.UpdatedAtUtc = DateTimeOffset.UtcNow;

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