namespace HabloTruckPlatform.Domain.Models;

public sealed class Company
{
    public required string CompanyId { get; init; }

    public string? Name { get; set; }

    // Admin / owner contact for the fleet pack
    public string? AdminEmailNormalized { get; set; }

    // Stripe linkage (for B2B purchases)
    public string? StripeCustomerId { get; set; }

    public string Status { get; set; } = "active"; // active / disabled

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}