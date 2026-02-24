using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = Buckets.StripeCustomerLookupPk(customerId) (e.g., HT#SC#41)
// RK = customerId
public sealed class UserStripeCustomerLookupEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string UserPk { get; set; }
    public required string UserId { get; set; }
}