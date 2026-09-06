using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableStripeEventAuditStore : IStripeEventAuditStore
{
    private readonly TableClient _table;
    private readonly ILogger<TableStripeEventAuditStore> _logger;

    public TableStripeEventAuditStore(TableServiceClient serviceClient, ILogger<TableStripeEventAuditStore>? logger = null)
    {
        _table = serviceClient.GetTableClient(TableNames.StripeEventAudit);
        _logger = logger ?? NullLogger<TableStripeEventAuditStore>.Instance;
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _table.CreateIfNotExistsAsync(ct);

    public async Task AppendAsync(StripeEventAuditItem item, CancellationToken ct = default)
    {
        var pk = $"{TablePrefixes.StripeEventAudit}_{item.EventCreatedUtc:yyyyMMdd}";
        var rk = $"{item.EventCreatedUtc.Ticks:D19}_{item.StripeEventId}_{Guid.NewGuid():N}";

        var entity = new StripeEventAuditEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            StripeEventId = item.StripeEventId,
            EventType = item.EventType,
            CustomerId = item.CustomerId,
            SubscriptionId = item.SubscriptionId,
            PriceId = item.PriceId,
            Status = item.Status,
            EventCreatedUtc = item.EventCreatedUtc,
            ProcessedUtc = item.ProcessedUtc,
            Outcome = item.Outcome,
            Reason = item.Reason,
            UserPk = item.UserPk,
            UserId = item.UserId,
            CurrentPeriodEndUtc = item.CurrentPeriodEndUtc,
            CancelAtPeriodEnd = item.CancelAtPeriodEnd,
            AccessMode = item.AccessMode,
            AccessSource = item.AccessSource,
            Error = item.Error
        };

        var watch = Stopwatch.StartNew();
        await _table.AddEntityAsync(entity, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} DurationMs={DurationMs} Success={Success}",
            "persistence",
            "stripe_event_audit.append",
            _table.Name,
            pk,
            watch.ElapsedMilliseconds,
            true);
    }

    public async Task<IReadOnlyList<StripeEventAuditItem>> GetByEventIdAsync(
        string stripeEventId,
        CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var results = new List<StripeEventAuditItem>();

        await foreach (var row in _table.QueryAsync<StripeEventAuditEntity>(
            filter: $"StripeEventId eq '{stripeEventId}'",
            cancellationToken: ct))
        {
            results.Add(ToModel(row));
        }

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Count={Count}",
            "persistence",
            "stripe_event_audit.query_by_event_id",
            _table.Name,
            watch.ElapsedMilliseconds,
            results.Count);

        return results;
    }

    public async Task<IReadOnlyList<StripeEventAuditItem>> QueryRecentAsync(
        DateTimeOffset dayUtc,
        int take = 100,
        CancellationToken ct = default)
    {
        var pk = $"{TablePrefixes.StripeEventAudit}_{dayUtc:yyyyMMdd}";
        var watch = Stopwatch.StartNew();
        var results = new List<StripeEventAuditItem>();

        await foreach (var row in _table.QueryAsync<StripeEventAuditEntity>(
            filter: $"PartitionKey eq '{pk}'",
            maxPerPage: take,
            cancellationToken: ct))
        {
            results.Add(ToModel(row));
            if (results.Count >= take) break;
        }

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} DurationMs={DurationMs} Count={Count}",
            "persistence",
            "stripe_event_audit.query_recent",
            _table.Name,
            pk,
            watch.ElapsedMilliseconds,
            results.Count);

        return results.OrderByDescending(x => x.EventCreatedUtc).ToList();
    }

    private static StripeEventAuditItem ToModel(StripeEventAuditEntity e)
        => new(
            e.StripeEventId,
            e.EventType,
            e.CustomerId,
            e.SubscriptionId,
            e.PriceId,
            e.Status,
            e.EventCreatedUtc ?? default,
            e.ProcessedUtc,
            LogContext.NormalizeOutcomeAlias(e.Outcome) ?? LogContext.Outcomes.Completed,
            e.Reason,
            e.UserPk,
            e.UserId,
            e.CurrentPeriodEndUtc,
            e.CancelAtPeriodEnd,
            e.AccessMode,
            e.AccessSource,
            e.Error
        );
}

