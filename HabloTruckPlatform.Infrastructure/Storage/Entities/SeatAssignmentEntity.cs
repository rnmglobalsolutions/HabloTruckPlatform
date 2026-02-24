using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT#SEAT#{CompanyId}
// RK = UserId
public sealed class SeatAssignmentEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string CompanyId { get; set; }
    public required string UserId { get; set; }

    public required string EntitlementId { get; set; }
    public string Status { get; set; } = "active";

    public DateTimeOffset AssignedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}