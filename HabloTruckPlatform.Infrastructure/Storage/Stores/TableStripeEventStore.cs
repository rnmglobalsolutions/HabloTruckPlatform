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

    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableStripeEventStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient Table => _factory.GetClient(TableNames.StripeEvents);

    /// <summary>
    /// Returns true if this call marked the event as processed for the first time.
    /// Returns false if already processed (idempotency).
    /// Must be atomic (insert-if-not-exists).
    /// </summary>
    public async Task<bool> TryMarkProcessedAsync(
        string stripeEventId,
        string eventType,
        DateTimeOffset createdUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("stripeEventId is required.", nameof(stripeEventId));

        var entity = new StripeEventEntity
        {
            PartitionKey = Pk,
            RowKey = stripeEventId.Trim(),
            EventType = string.IsNullOrWhiteSpace(eventType) ? string.Empty : eventType.Trim(),
            CreatedUtc = createdUtc.UtcDateTime,
            ProcessedAtUtc = DateTime.UtcNow
        };

        // Atomic insert-if-not-exists (409 => already exists)
        return await _repo.TryInsertAsync(Table, entity, ct);
    }
}