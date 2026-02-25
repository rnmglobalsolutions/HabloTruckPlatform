using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT#INVC#{CompanyId}
// RK = {CreatedAtTicks:D19}_{Code}
public sealed class InviteCompanyIndexEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string CompanyId { get; set; }
    public required string Code { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string Status { get; set; } = "active";
}