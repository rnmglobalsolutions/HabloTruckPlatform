namespace HabloTruckPlatform.Functions.Contracts;

public sealed class RequeueFailedActionHttpRequest
{
    public string? Pk { get; set; }
    public string? Rk { get; set; }
    public int? DelayMinutes { get; set; } // default 1
}