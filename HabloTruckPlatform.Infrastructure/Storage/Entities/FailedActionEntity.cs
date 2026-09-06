using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT_FA_yyyyMMddHH   (bucket por hora para retry barato)
// RK = {NextRetryTicks:D19}_{Ulid}  (ordena por due time)
public sealed class FailedActionEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string ActionType { get; set; }      // "manychat.sync"
    public required string PayloadJson { get; set; }     // json con data mínima
    public int Attempts { get; set; }
    public string Status { get; set; } = "pending";      // pending/succeeded/dead
    public DateTimeOffset NextRetryUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? LastError { get; set; }
}