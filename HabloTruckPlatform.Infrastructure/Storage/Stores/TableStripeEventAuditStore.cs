using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableStripeEventAuditStore : IStripeEventAuditStore
{
    private readonly TableClient _table;

    public TableStripeEventAuditStore(TableServiceClient serviceClient)
    {
        _table = serviceClient.GetTableClient(TableNames.StripeEventAudit);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _table.CreateIfNotExistsAsync(ct);

    public async Task AppendAsync(StripeEventAuditItem item, CancellationToken ct = default)
    {
        var pk = $"{TablePrefixes.StripeEventAudit}#{item.EventCreatedUtc:yyyyMMdd}";
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

        await _table.AddEntityAsync(entity, ct);
    }

    public async Task<IReadOnlyList<StripeEventAuditItem>> GetByEventIdAsync(
        string stripeEventId,
        CancellationToken ct = default)
    {
        var results = new List<StripeEventAuditItem>();

        await foreach (var row in _table.QueryAsync<StripeEventAuditEntity>(
            filter: $"StripeEventId eq '{stripeEventId}'",
            cancellationToken: ct))
        {
            results.Add(ToModel(row));
        }

        return results;
    }

    public async Task<IReadOnlyList<StripeEventAuditItem>> QueryRecentAsync(
        DateTimeOffset dayUtc,
        int take = 100,
        CancellationToken ct = default)
    {
        var pk = $"{TablePrefixes.StripeEventAudit}#{dayUtc:yyyyMMdd}";
        var results = new List<StripeEventAuditItem>();

        await foreach (var row in _table.QueryAsync<StripeEventAuditEntity>(
            filter: $"PartitionKey eq '{pk}'",
            maxPerPage: take,
            cancellationToken: ct))
        {
            results.Add(ToModel(row));
            if (results.Count >= take) break;
        }

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
            e.Outcome,
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