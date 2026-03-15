namespace HabloTruckPlatform.Application.Models;

public sealed class StripePaymentMethodUpdateLinkResult
{
    public bool Result { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string? CustomerId { get; set; }
    public string? SubscriptionId { get; set; }
    public bool ImmediateRetryRecommended { get; set; }
    public string? Error { get; set; }
}
