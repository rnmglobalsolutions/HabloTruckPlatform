using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableGraceIndexStore : IGraceIndexStore
{
    private readonly TableClient _grace;

    public TableGraceIndexStore(TableServiceClient serviceClient)
    {
        _grace = serviceClient.GetTableClient(TableNames.GraceIndex);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _grace.CreateIfNotExistsAsync(ct);

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

        await _grace.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    /// <summary>
    /// Best-effort delete. Because we don't store "current grace PK" on the user,
    /// we search a rolling window of hour buckets for the row and delete if found.
    /// </summary>
    public async Task DeleteForUserAsync(UserRef userRef, CancellationToken ct = default)
    {
        // If you want to make this O(1), store CurrentGracePk + CurrentGraceRk on UserEntity.
        // For now we do best-effort scanning a short window.

        var nowUtc = DateTimeOffset.UtcNow;

        // Scan last 120 hours (5 days) as a safe window.
        // If your grace max is 72h, this is enough + buffer.
        const int lookbackHours = 120;

        for (int i = 0; i <= lookbackHours; i++)
        {
            ct.ThrowIfCancellationRequested();

            var hour = nowUtc.AddHours(-i);
            var pk = $"{TablePrefixes.Grace}#{hour:yyyyMMddHH}";

            // Query for this userId in that partition
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and UserId eq {userRef.UserId}");

            try
            {
                await foreach (var e in _grace.QueryAsync<GraceIndexEntity>(filter: filter, maxPerPage: 50, cancellationToken: ct))
                {
                    // delete exact row key
                    await _grace.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, ct);
                }
            }
            catch
            {
                // ignore bucket errors
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

        // RK starts with ticks => we can range-query by RowKey
        // RowKey <= "{nowTicks:D19}_~~~~" (tilde sorts high)
        var maxRk = $"{nowUtc.Ticks:D19}_~~~~";

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {gracePk} and RowKey le {maxRk}");

        var results = new List<GraceIndexItem>(Math.Min(take, 500));

        await foreach (var e in _grace.QueryAsync<GraceIndexEntity>(filter: filter, maxPerPage: take, cancellationToken: ct))
        {
            results.Add(new GraceIndexItem(e.UserPk, e.UserId, e.GraceEndsAtUtc));
            if (results.Count >= take) break;
        }

        return results;
    }

    private static string GracePk(DateTimeOffset graceEndsAtUtc)
        => $"{TablePrefixes.Grace}#{graceEndsAtUtc:yyyyMMddHH}";

    private static string GraceRk(DateTimeOffset graceEndsAtUtc, string userId)
        => $"{graceEndsAtUtc.Ticks:D19}_{userId}";

    public async Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pk) || string.IsNullOrWhiteSpace(rk))
            return;

        try
        {
            await _grace.DeleteEntityAsync(pk, rk, ETag.All, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // already gone
        }
    }
}