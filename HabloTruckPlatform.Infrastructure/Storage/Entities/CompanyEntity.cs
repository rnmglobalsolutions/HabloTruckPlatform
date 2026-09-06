using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT_C
// RK = CompanyId
public sealed class CompanyEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? Name { get; set; }
    public string? AdminEmailNormalized { get; set; }
    public string? StripeCustomerId { get; set; }
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}