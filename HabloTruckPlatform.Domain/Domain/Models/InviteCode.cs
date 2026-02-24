namespace HabloTruckPlatform.Domain.Models;

public sealed class InviteCode
{
    public required string CompanyId { get; init; }
    public required string Code { get; init; }
    public required string EntitlementId { get; init; }

    public required int MaxUses { get; init; }
    public required int Uses { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public string Status { get; set; } = "active"; // active / disabled

    public bool IsActive(DateTimeOffset nowUtc)
        => Status == "active"
           && Uses < MaxUses
           && (ExpiresAtUtc is null || ExpiresAtUtc > nowUtc);

    public void RegisterUse()
    {
        if (Uses >= MaxUses)
            throw new InvalidOperationException("Invite code capacity reached.");

        Uses++;
    }
}