namespace HabloTruckPlatform.Functions.Contracts;

public sealed class StartFleetCheckoutHttpRequest
{
    public string? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public string? Email { get; set; }
    public string? PhoneE164 { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public int Seats { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
}
