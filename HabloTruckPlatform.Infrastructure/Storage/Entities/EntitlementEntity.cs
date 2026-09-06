using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT_ENT_{CompanyId}
// RK = EntitlementId
public sealed class EntitlementEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string CompanyId { get; set; }
    public required string EntitlementId { get; set; }

    public int SeatsTotal { get; set; }
    public int SeatsUsed { get; set; }
    public bool IsOverCapacity { get; set; }

    public string Status { get; set; } = "active"; // active/past_due/expired/refunded/disabled

    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
}
