namespace HabloTruckPlatform.Application.Models;

public sealed class StripeCheckoutSessionResult
{
    public bool Result { get; set; }
    public string Url { get; set; } = "";
    public string? SessionId { get; set; }
    public string? GeneratedAtUtc { get; set; }
    public string? Error { get; set; }
}
