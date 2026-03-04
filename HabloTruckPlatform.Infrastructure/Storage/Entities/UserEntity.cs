using Azure;
using Azure.Data.Tables;

namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

public sealed class UserEntity : ITableEntity
{
    public required string PartitionKey { get; set; }
    public required string RowKey { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Identity
    public string? EmailNormalized { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? PhoneE164 { get; set; }

    // Stripe
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }

    // ✅ NEW: upgrades/downgrades
    public string? StripePriceId { get; set; }
    public DateTimeOffset? StripeCurrentPeriodEndUtc { get; set; }
    public bool? StripeCancelAtPeriodEnd { get; set; }
    public string? IndividualPlanTerm { get; set; } // "monthly"|"annual"

    // optional analytics
    public string? PlanType { get; set; }           // "individual"|"fleet"|"cdl_cohort"
    public string? CohortId { get; set; }
    public string? SchoolId { get; set; }

    // Individual grace
    public DateTimeOffset? IndividualGraceEndsAtUtc { get; set; }

    // Company seat facts
    public string? CompanyId { get; set; }
    public string? SeatEntitlementId { get; set; }
    public string? SeatStatus { get; set; } // active/revoked/none

    // Effective snapshot
    public string? EffectiveAccessMode { get; set; }
    public int? EffectiveAccessSource { get; set; }
    public DateTimeOffset? EffectiveGraceEndsAtUtc { get; set; }

    // Audit
    public string? LastStripeEventId { get; set; }
    public DateTimeOffset? LastStripeEventCreatedUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Grace pointers
    public string? CurrentGracePk { get; set; }
    public string? CurrentGraceRk { get; set; }
}