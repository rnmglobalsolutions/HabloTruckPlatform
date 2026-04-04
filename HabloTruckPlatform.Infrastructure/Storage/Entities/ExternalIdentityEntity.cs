using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = Buckets.ExternalIdentityPk(userId)
// RK = provider|externalSubject
public sealed class ExternalIdentityEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string UserId { get; set; }
    public required string Provider { get; set; }
    public required string ExternalSubject { get; set; }
    public required string Channel { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public string? MetadataJson { get; set; }
}
