using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT_EE_yyyyMMdd (based on EndUtc date)
// RK = {EndTicks:D19}_{CompanyId}_{EntitlementId}
public sealed class EntitlementExpiryIndexEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string CompanyId { get; set; }
    public required string EntitlementId { get; set; }
    public required DateTimeOffset EndUtc { get; set; }
}