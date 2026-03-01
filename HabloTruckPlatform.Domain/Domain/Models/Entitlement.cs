namespace HabloTruckPlatform.Domain.Models;

public sealed class Entitlement
{
    public required string CompanyId { get; init; }
    public required string EntitlementId { get; init; }

    public required int SeatsTotal { get; init; }
    public required int SeatsUsed { get; set; }

    public required string Status { get; set; } // "active", "expired", "refunded"

    public required DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; } // null = lifetime

    public bool IsActive(DateTimeOffset nowUtc)
        => Status.Equals("active", StringComparison.OrdinalIgnoreCase)
           && (EndUtc is null || EndUtc > nowUtc);

    public bool IsExpired(DateTimeOffset nowUtc)
        => EndUtc is not null && EndUtc <= nowUtc;

    public bool IsLifetime => EndUtc is null;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}