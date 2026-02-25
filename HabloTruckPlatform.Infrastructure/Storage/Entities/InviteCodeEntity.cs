using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

// PK = HT#INV#{prefix} (prefix = first 2 chars of code, normalized)
// RK = code
public sealed class InviteCodeEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public required string Code { get; set; }
    public required string CompanyId { get; set; }
    public required string EntitlementId { get; set; }

    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int MaxUses { get; set; }
    public int Uses { get; set; }

    public string? CreatedBy { get; set; }
}