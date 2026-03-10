using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Storage;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableGraceIndexStore : IGraceIndexStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableGraceIndexStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient GraceTable => _factory.GetClient(TableNames.GraceIndex);

    public Task EnsureTableAsync(CancellationToken ct = default)
        => _factory.EnsureTableAsync(TableNames.GraceIndex, ct);

    public async Task UpsertAsync(UserRef userRef, DateTimeOffset graceEndsAtUtc, CancellationToken ct = default)
    {
        var pk = GracePk(graceEndsAtUtc);
        var rk = GraceRk(graceEndsAtUtc, userRef.UserId);

        var entity = new GraceIndexEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            UserPk = userRef.UserPk,
            UserId = userRef.UserId,
            GraceEndsAtUtc = graceEndsAtUtc
        };

        // Replace is fine here since entity is deterministic
        await _repo.UpsertAsync(GraceTable, entity, TableUpdateMode.Replace, ct);
    }

    /// <summary>
    /// Best-effort legacy delete (scan window). Prefer DeleteAsync(pk,rk) using pointers stored on the user.
    /// </summary>
    public async Task DeleteForUserAsync(UserRef userRef, CancellationToken ct = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        // Scan last N hours.
        // If your grace is 7 days, increase this to 24*8 or 24*10.
        const int lookbackHours = 24 * 10; // 10 days buffer (safe)

        for (int i = 0; i <= lookbackHours; i++)
        {
            ct.ThrowIfCancellationRequested();

            var hour = nowUtc.AddHours(-i);
            var pk = $"{TablePrefixes.Grace}_{hour:yyyyMMddHH}";

            // Query by PK and UserId
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and UserId eq {userRef.UserId}");

            try
            {
                await foreach (var e in GraceTable.QueryAsync<GraceIndexEntity>(
                                   filter: filter,
                                   maxPerPage: 50,
                                   cancellationToken: ct))
                {
                    // O(1) delete per found entity
                    await _repo.DeleteIfExistsAsync(GraceTable, e.PartitionKey, e.RowKey, ct);
                }
            }
            catch
            {
                // best-effort: ignore bucket errors
            }
        }
    }

    public async Task<IReadOnlyList<GraceIndexItem>> QueryExpiredAsync(
        string gracePk,
        DateTimeOffset nowUtc,
        int take = 500,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gracePk))
            throw new ArgumentException("gracePk is required.", nameof(gracePk));

        // RK starts with ticks => range query by RowKey
        var maxRk = $"{nowUtc.Ticks:D19}_~~~~";

        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {gracePk} and RowKey le {maxRk}");

        var results = new List<GraceIndexItem>(Math.Min(take, 500));

        await foreach (var e in GraceTable.QueryAsync<GraceIndexEntity>(
                           filter: filter,
                           maxPerPage: take,
                           cancellationToken: ct))
        {
            results.Add(new GraceIndexItem(e.UserPk, e.UserId, e.GraceEndsAtUtc));
            if (results.Count >= take) break;
        }

        return results;
    }

    public async Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pk) || string.IsNullOrWhiteSpace(rk))
            return;

        // Best-effort delete
        await _repo.DeleteIfExistsAsync(GraceTable, pk, rk, ct);
    }

    private static string GracePk(DateTimeOffset graceEndsAtUtc)
        => $"{TablePrefixes.Grace}_{graceEndsAtUtc:yyyyMMddHH}";

    private static string GraceRk(DateTimeOffset graceEndsAtUtc, string userId)
        => $"{graceEndsAtUtc.Ticks:D19}_{userId}";
}