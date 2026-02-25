namespace HabloTruckPlatform.Functions.Contracts;

public sealed class JoinCompanyHttpRequest
{
    public string? InviteCode { get; set; }
    public string? ManyChatSubscriberId { get; set; }
    public string? Email { get; set; }
    public string? PhoneE164 { get; set; }
}