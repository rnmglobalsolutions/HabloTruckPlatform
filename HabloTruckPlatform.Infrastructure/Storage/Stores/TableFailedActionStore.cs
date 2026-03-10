using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Infrastructure.Storage;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableFailedActionStore : IFailedActionStore
{
    #region Payload Minimo para Manychat
    /*
     * {
     *   "userPk": "HT_U_003",
     *   "userId": "01HRF...",
     *   "reason": "sync_access"
     * }
     */
    #endregion

    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableFailedActionStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient Table => _factory.GetClient(TableNames.FailedActions);

    public Task EnsureTableAsync(CancellationToken ct = default)
        => _factory.EnsureTableAsync(TableNames.FailedActions, ct);

    public async Task EnqueueAsync(
        string actionType,
        string payloadJson,
        DateTimeOffset nextRetryUtc,
        CancellationToken ct = default)
    {
        var pk = Pk(nextRetryUtc);
        var ulid = UlidIds.NewFailedActionId();
        var rk = $"{nextRetryUtc.Ticks:D19}_{ulid}";

        var now = DateTimeOffset.UtcNow;

        var e = new FailedActionEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            ActionType = actionType,
            PayloadJson = payloadJson,
            Attempts = 0,
            Status = "pending",
            NextRetryUtc = nextRetryUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        // Insert-only
        var inserted = await _repo.TryInsertAsync(Table, e, ct);
        if (!inserted)
            throw new InvalidOperationException($"FailedAction already exists. PK={pk}, RK={rk}");
    }

    public async Task<IReadOnlyList<FailedActionItem>> GetDueAsync(
        DateTimeOffset nowUtc,
        int lookbackHours,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) take = 100;
        if (lookbackHours < 1) lookbackHours = 1;

        var results = new List<FailedActionItem>(take);

        // Scan hour buckets from now backwards
        for (int h = 0; h <= lookbackHours; h++)
        {
            ct.ThrowIfCancellationRequested();

            var hour = nowUtc.AddHours(-h);
            var pk = Pk(hour);

            var maxRk = $"{nowUtc.Ticks:D19}_~~~~";
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk} and RowKey le {maxRk}");

            await foreach (var e in Table.QueryAsync<FailedActionEntity>(
                               filter: filter,
                               maxPerPage: take,
                               cancellationToken: ct))
            {
                if (!string.Equals(e.Status, "pending", StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new FailedActionItem(
                    e.PartitionKey,
                    e.RowKey,
                    e.ActionType,
                    e.PayloadJson,
                    e.Attempts,
                    e.NextRetryUtc));

                if (results.Count >= take)
                    return results;
            }
        }

        return results;
    }

    public async Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default)
    {
        var e = await _repo.GetOrNullAsync<FailedActionEntity>(Table, pk, rk, ct);
        if (e is null) return;

        e.Status = "succeeded";
        e.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await Table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
    }

    public async Task RescheduleAsync(
        string pk,
        string rk,
        int attempts,
        DateTimeOffset nextRetryUtc,
        string lastError,
        CancellationToken ct = default)
    {
        var e = await _repo.GetOrNullAsync<FailedActionEntity>(Table, pk, rk, ct);
        if (e is null) return;

        e.Attempts = attempts;
        e.LastError = Trunc(lastError, 2000);
        e.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // IMPORTANT: NextRetryUtc changes ordering -> move row to new PK/RK.
        // We close current row and enqueue a new one.
        e.Status = "succeeded";
        await Table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);

        await EnqueueAsync(e.ActionType, e.PayloadJson, nextRetryUtc, ct);
    }

    public async Task MarkDeadAsync(
        string pk,
        string rk,
        int attempts,
        string lastError,
        CancellationToken ct = default)
    {
        var e = await _repo.GetOrNullAsync<FailedActionEntity>(Table, pk, rk, ct);
        if (e is null) return;

        e.Attempts = attempts;
        e.LastError = Trunc(lastError, 2000);
        e.Status = "dead";
        e.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await Table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);
    }

    public async Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(
        DateTimeOffset nowUtc,
        string status,
        int lookbackHours,
        int take,
        CancellationToken ct = default)
    {
        if (take <= 0) take = 100;
        if (lookbackHours < 1) lookbackHours = 1;

        status = status.Trim().ToLowerInvariant();

        var results = new List<FailedActionItem>(take);

        for (int h = 0; h <= lookbackHours; h++)
        {
            ct.ThrowIfCancellationRequested();

            var hour = nowUtc.AddHours(-h);
            var pk = Pk(hour);

            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {pk}");

            await foreach (var e in Table.QueryAsync<FailedActionEntity>(
                               filter: filter,
                               maxPerPage: 200,
                               cancellationToken: ct))
            {
                if (!string.Equals(e.Status, status, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new FailedActionItem(
                    e.PartitionKey,
                    e.RowKey,
                    e.ActionType,
                    e.PayloadJson,
                    e.Attempts,
                    e.NextRetryUtc));

                if (results.Count >= take)
                    return results;
            }
        }

        // most recent first
        return results
            .OrderByDescending(x => x.Rk)
            .Take(take)
            .ToList();
    }

    public async Task RequeueAsync(
        string pk,
        string rk,
        DateTimeOffset nextRetryUtc,
        CancellationToken ct = default)
    {
        var e = await _repo.GetOrNullAsync<FailedActionEntity>(Table, pk, rk, ct);
        if (e is null) return;

        // close current row
        e.Status = "succeeded";
        e.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await Table.UpdateEntityAsync(e, e.ETag, TableUpdateMode.Replace, ct);

        // enqueue new item (attempts reset)
        await EnqueueAsync(e.ActionType, e.PayloadJson, nextRetryUtc, ct);
    }

    private static string Pk(DateTimeOffset utc) => $"{TablePrefixes.FailedAction}_{utc:yyyyMMddHH}";

    private static string Trunc(string s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}