

using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

public sealed class UserEntity : ITableEntity
{
    // PK: HT#U#NNN (bucket), RK: UserId (ULID/GUID string)
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Identity
    public string? EmailNormalized { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? PhoneE164 { get; set; }

    // Stripe facts
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }

    // Individual grace
    public DateTimeOffset? IndividualGraceEndsAtUtc { get; set; }

    // Company seat facts
    public string? CompanyId { get; set; }
    public string? SeatEntitlementId { get; set; }
    public string? SeatStatus { get; set; } // active/revoked/none

    // Effective snapshot (projection)
    public string? EffectiveAccessMode { get; set; }    // "Full"|"Grace"|"Blocked"
    public int? EffectiveAccessSource { get; set; }     // AccessSource flags int
    public DateTimeOffset? EffectiveGraceEndsAtUtc { get; set; }

    // Audit / ordering guard
    public string? LastStripeEventId { get; set; }
    public DateTimeOffset? LastStripeEventCreatedUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Grace Period
    public string? CurrentGracePk { get; set; }
    public string? CurrentGraceRk { get; set; }
}