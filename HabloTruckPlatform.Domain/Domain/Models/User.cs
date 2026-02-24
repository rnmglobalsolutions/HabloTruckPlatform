using HabloTruckPlatform.Domain.Access;

namespace HabloTruckPlatform.Domain.Models;

public sealed class User
{
    // Stable internal identifier (ULID/GUID string)
    public required string UserId { get; init; }

    // Identity / Contact
    public string? EmailNormalized { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? PhoneE164 { get; set; }

    // -----------------------
    // Individual subscription facts (Stripe)
    // -----------------------
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    /// <summary>
    /// Stripe subscription status snapshot: "active", "past_due", "canceled", "unpaid", "deleted", etc.
    /// Keep it as string to avoid tight coupling; map/validate in Application layer.
    /// </summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>
    /// Individual grace end (48–72h policy). Null means no grace active.
    /// </summary>
    public DateTimeOffset? IndividualGraceEndsAtUtc { get; set; }

    // -----------------------
    // Company seat facts (B2B packs)
    // -----------------------
    public string? CompanyId { get; set; }
    public string? SeatEntitlementId { get; set; }

    /// <summary>
    /// Seat status: "active", "revoked", "none"
    /// (Optional: use an enum in domain if you prefer.)
    /// </summary>
    public string? SeatStatus { get; set; }

    // -----------------------
    // Computed snapshot (projection)
    // -----------------------
    public AccessSnapshot? EffectiveAccess { get; set; }

    // -----------------------
    // Audit / ordering guards (domain-friendly)
    // -----------------------
    public string? LastStripeEventId { get; set; }
    public DateTimeOffset? LastStripeEventCreatedUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Convenience helpers (optional)
    public bool HasActiveSeat()
        => string.Equals(SeatStatus, "active", StringComparison.OrdinalIgnoreCase);

    public bool IsIndividualActive()
        => string.Equals(SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase);

    public bool IsIndividualGraceActive(DateTimeOffset nowUtc)
        => IndividualGraceEndsAtUtc is not null && IndividualGraceEndsAtUtc > nowUtc;
}