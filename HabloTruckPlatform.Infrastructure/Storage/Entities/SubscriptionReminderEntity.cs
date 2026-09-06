using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT_REM_{bucket}
// RK = {subscriptionId}_{reminderType}_{periodEnd:yyyyMMdd}
public sealed class SubscriptionReminderEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string SubscriptionId { get; set; }
    public required string ReminderType { get; set; }
    public required DateTimeOffset PeriodEndUtc { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }
}
