using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableSeatAssignmentStore : ISeatAssignmentStore
{
    private readonly TableClient _seats;

    public TableSeatAssignmentStore(TableServiceClient serviceClient)
    {
        _seats = serviceClient.GetTableClient(TableNames.Seats);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _seats.CreateIfNotExistsAsync(ct);

    public async Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(userId))
            return null;

        try
        {
            var pk = SeatAssignmentMapper.Pk(companyId.Trim());
            var rk = userId.Trim();

            var resp = await _seats.GetEntityAsync<SeatAssignmentEntity>(pk, rk, cancellationToken: ct);
            return SeatAssignmentMapper.FromEntity(resp.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
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
        await _seats.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
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

        // PartitionKey eq pk AND Status eq 'active' AND EntitlementId eq entitlementId
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {pk} and Status eq {"active"} and EntitlementId eq {entitlementId.Trim()}");

        var count = 0;

        await foreach (var _ in _seats.QueryAsync<SeatAssignmentEntity>(filter: filter, maxPerPage: 1000, cancellationToken: ct))
        {
            count++;
        }

        return count;
    }
}