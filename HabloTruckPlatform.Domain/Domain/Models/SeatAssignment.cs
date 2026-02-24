namespace HabloTruckPlatform.Domain.Models;

public sealed class SeatAssignment
{
    public required string CompanyId { get; init; }
    public required string UserId { get; init; }
    public required string EntitlementId { get; init; }

    public required string Status { get; set; } // "active", "revoked"
    public required DateTimeOffset AssignedAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; set; }

    public bool IsActive()
        => Status.Equals("active", StringComparison.OrdinalIgnoreCase)
           && RevokedAtUtc is null;
}