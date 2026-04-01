namespace HabloTruckPlatform.Application.Models;

public sealed class CompanyAdminInviteResult
{
    public bool Result { get; set; }
    public bool Found { get; set; }
    public bool Created { get; set; }
    public bool Updated { get; set; }
    public bool Resent { get; set; }
    public bool Valid { get; set; }
    public string? Error { get; set; }

    public string? CompanyId { get; set; }
    public string? EntitlementId { get; set; }
    public string? Code { get; set; }
    public string? Status { get; set; }
    public string? CreatedBy { get; set; }

    public int MaxUses { get; set; }
    public int Uses { get; set; }
    public int Remaining { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
