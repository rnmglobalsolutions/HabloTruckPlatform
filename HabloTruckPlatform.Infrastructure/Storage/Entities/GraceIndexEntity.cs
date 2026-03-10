using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT_GRACE_yyyyMMddHH
// RK = {ticks:D19}_{userId}
public sealed class GraceIndexEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string UserPk { get; set; }
    public required string UserId { get; set; }

    public required DateTimeOffset GraceEndsAtUtc { get; set; }
}