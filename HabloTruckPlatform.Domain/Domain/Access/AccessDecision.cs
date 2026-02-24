// Domain/Access/AccessDecision.cs
//
// This is the single "output" object your domain access rules produce.
// Store it as a snapshot on the User entity if you want (AccessModeEffective),
// and project it to ManyChat tags/fields.

namespace HabloTruckPlatform.Domain.Access;

public sealed record AccessDecision(
    AccessMode Mode,
    AccessSource Source,
    DateTimeOffset? GraceEndsAtUtc,
    string Reason)
{
    public bool HasFullAccess => Mode == AccessMode.Full;
    public bool IsGrace => Mode == AccessMode.Grace;
    public bool IsBlocked => Mode == AccessMode.Blocked;
}