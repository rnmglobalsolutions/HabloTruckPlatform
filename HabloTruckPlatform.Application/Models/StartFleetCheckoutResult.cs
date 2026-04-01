namespace HabloTruckPlatform.Application.Models;

public sealed class StartFleetCheckoutResult
{
    public bool Result { get; set; }
    public string Url { get; set; } = "";
    public string? SessionId { get; set; }
    public string? Error { get; set; }
    public string? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int Seats { get; set; }
}
