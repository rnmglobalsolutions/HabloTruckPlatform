namespace HabloTruckPlatform.Application.Models;

public sealed record GraceIndexItem(
    string UserPk,
    string UserId,
    DateTimeOffset GraceEndsAtUtc);