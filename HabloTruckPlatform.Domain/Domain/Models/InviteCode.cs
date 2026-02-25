namespace HabloTruckPlatform.Domain.Models;

public sealed class InviteCode
{
    public required string Code { get; set; }           // e.g. "HT-AB12CD"
    public required string CompanyId { get; set; }
    public required string EntitlementId { get; set; }

    public string Status { get; set; } = "active";      // active/disabled
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int MaxUses { get; set; } = 25;
    public int Uses { get; set; } = 0;

    // optional: who created it
    public string? CreatedBy { get; set; }
}