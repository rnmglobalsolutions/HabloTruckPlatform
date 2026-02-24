using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = Buckets.ManyChatLookupPk(subscriberId) (e.g., HT#MC#13)
// RK = subscriberId
public sealed class UserManyChatLookupEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string UserPk { get; set; }
    public required string UserId { get; set; }
}