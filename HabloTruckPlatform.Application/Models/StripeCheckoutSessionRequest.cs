namespace HabloTruckPlatform.Application.Models;

public sealed class StripeCheckoutSessionRequest
{
    public string PriceId { get; set; } = "";
    public int Quantity { get; set; } = 1;

    public string SuccessUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";

    // individual | fleet | cdl_cohort | testing
    public string? PlanType { get; set; }

    // identity / lead data
    public string? Email { get; set; }
    public string? PhoneE164 { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? ManyChatChannel { get; set; }

    // company / b2b
    public string? CompanyId { get; set; }
    public string? CompanyName { get; set; }

    // cohort
    public string? SchoolId { get; set; }
    public string? CohortId { get; set; }

    // optional metadata
    public int Seats { get; set; }
    public int DurationDays { get; set; }
}
