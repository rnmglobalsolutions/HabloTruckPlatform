using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

public sealed class StripeEventAuditEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string StripeEventId { get; set; } = default!;
    public string EventType { get; set; } = default!;

    public string? CustomerId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? PriceId { get; set; }
    public string? Status { get; set; }

    public DateTimeOffset? EventCreatedUtc { get; set; }
    public DateTimeOffset ProcessedUtc { get; set; }

    public string Outcome { get; set; } = default!;
    // applied | ignored_duplicate | ignored_out_of_order | failed | replayed

    public string? Reason { get; set; }

    public string? UserPk { get; set; }
    public string? UserId { get; set; }

    public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
    public bool? CancelAtPeriodEnd { get; set; }

    public string? AccessMode { get; set; }
    public string? AccessSource { get; set; }

    public string? Error { get; set; }
}