using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableStripeEventStore : IStripeEventStore
{
    private const string Pk = "HT#STRIPE#EVENT";
    private readonly TableClient _events;

    public TableStripeEventStore(TableServiceClient serviceClient)
    {
        _events = serviceClient.GetTableClient(TableNames.StripeEvents);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _events.CreateIfNotExistsAsync(ct);

    public async Task<bool> TryMarkProcessedAsync(
        string stripeEventId,
        string eventType,
        DateTimeOffset createdUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeEventId))
            throw new ArgumentException("stripeEventId is required.", nameof(stripeEventId));

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("eventType is required.", nameof(eventType));

        var id = stripeEventId.Trim();

        var entity = new StripeEventEntity
        {
            PartitionKey = Pk,
            RowKey = id,
            EventType = eventType.Trim(),
            CreatedUtc = createdUtc,
            ProcessedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            // Atomic insert-if-not-exists
            await _events.AddEntityAsync(entity, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Already processed
            return false;
        }
    }
}