using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = "HT_STRIPE_EVENT"
// RK = stripeEventId
public sealed class StripeEventEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string EventType { get; set; }
    public required DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}