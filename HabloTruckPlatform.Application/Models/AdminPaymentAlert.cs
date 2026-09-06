namespace HabloTruckPlatform.Application.Models;

public sealed class AdminPaymentAlert
{
    public string EnvironmentName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public string FailureStage { get; set; } = "";
    public string FailureReason { get; set; } = "";
    public string Severity { get; set; } = "Critical";
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? UserPk { get; set; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public string? PhoneE164 { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? CompanyId { get; set; }

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeInvoiceId { get; set; }
    public string? StripeEventId { get; set; }
    public string? StripeCheckoutSessionId { get; set; }
    public string? PlanType { get; set; }
    public string? PriceId { get; set; }

    public Dictionary<string, string?> Details { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
