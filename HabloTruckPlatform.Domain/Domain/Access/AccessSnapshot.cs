namespace HabloTruckPlatform.Domain.Access;

public sealed record AccessSnapshot(
    AccessMode Mode,
    AccessSource Source,
    DateTimeOffset? GraceEndsAtUtc)
{
    public static AccessSnapshot FromDecision(AccessDecision d)
        => new(d.Mode, d.Source, d.GraceEndsAtUtc);
}