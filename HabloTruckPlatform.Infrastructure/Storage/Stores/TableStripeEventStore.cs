using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Infrastructure.Storage;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableStripeEventStore : IStripeEventStore
{
    // One PK is fine: Stripe event IDs are globally unique, and you do single-row inserts.
    private const string Pk = $"{TablePrefixes.Stripe}_EVT";
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(10);

    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableStripeEventStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient Table => _factory.GetClient(TableNames.StripeEvents);

    public async Task<StripeEventProcessingStartResult> TryStartProcessingAsync(
        string stripeEventId,
        string eventType,
        DateTimeOffset createdUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("stripeEventId is required.", nameof(stripeEventId));

        var rowKey = stripeEventId.Trim();
        var nowUtc = DateTimeOffset.UtcNow;
        var entity = new StripeEventEntity
        {
            PartitionKey = Pk,
            RowKey = rowKey,
            EventType = string.IsNullOrWhiteSpace(eventType) ? string.Empty : eventType.Trim(),
            CreatedUtc = createdUtc.UtcDateTime,
            Status = StripeEventEntity.StatusProcessing,
            ProcessingStartedAtUtc = nowUtc,
            ProcessingExpiresAtUtc = nowUtc.Add(ProcessingLease),
            ProcessedAtUtc = null
        };

        if (await _repo.TryInsertAsync(Table, entity, ct))
            return StripeEventProcessingStartResult.Started;

        while (true)
        {
            var existing = await _repo.GetOrNullAsync<StripeEventEntity>(Table, Pk, rowKey, ct);
            if (existing is null)
                return await TryStartProcessingAsync(stripeEventId, eventType, createdUtc, ct);

            if (string.Equals(existing.Status, StripeEventEntity.StatusProcessed, StringComparison.OrdinalIgnoreCase))
                return StripeEventProcessingStartResult.AlreadyProcessed;

            if (existing.ProcessingExpiresAtUtc is not null && existing.ProcessingExpiresAtUtc > nowUtc)
                return StripeEventProcessingStartResult.AlreadyInProgress;

            existing.EventType = string.IsNullOrWhiteSpace(eventType) ? existing.EventType : eventType.Trim();
            existing.CreatedUtc = createdUtc.UtcDateTime;
            existing.Status = StripeEventEntity.StatusProcessing;
            existing.ProcessingStartedAtUtc = nowUtc;
            existing.ProcessingExpiresAtUtc = nowUtc.Add(ProcessingLease);
            existing.ProcessedAtUtc = null;

            try
            {
                await Table.UpdateEntityAsync(existing, existing.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
                return StripeEventProcessingStartResult.Started;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                continue;
            }
        }
    }

    public async Task MarkProcessedAsync(string stripeEventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("stripeEventId is required.", nameof(stripeEventId));

        var entity = await _repo.GetOrNullAsync<StripeEventEntity>(Table, Pk, stripeEventId.Trim(), ct)
            ?? throw new InvalidOperationException($"Stripe event '{stripeEventId}' was not found for completion.");

        entity.Status = StripeEventEntity.StatusProcessed;
        entity.ProcessedAtUtc = DateTimeOffset.UtcNow;
        entity.ProcessingExpiresAtUtc = null;

        await Table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct).ConfigureAwait(false);
    }

    public async Task ReleaseProcessingAsync(string stripeEventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("stripeEventId is required.", nameof(stripeEventId));

        await _repo.DeleteIfExistsAsync(Table, Pk, stripeEventId.Trim(), ct);
    }
}
