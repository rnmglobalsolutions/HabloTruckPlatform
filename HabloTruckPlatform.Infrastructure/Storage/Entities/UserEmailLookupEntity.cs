using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = Buckets.EmailLookupPk(emailNormalized) (e.g., HT#EMAIL#ju)
// RK = emailNormalized
public sealed class UserEmailLookupEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Pointer to user
    public required string UserPk { get; set; }
    public required string UserId { get; set; }
}