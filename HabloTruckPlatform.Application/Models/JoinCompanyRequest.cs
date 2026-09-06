namespace HabloTruckPlatform.Application.Models;

public sealed record JoinCompanyRequest(
    string UserPk,
    string UserId,
    string CompanyId,
    string EntitlementId,
    string? InviteCode // optional if you validate invite codes later
);