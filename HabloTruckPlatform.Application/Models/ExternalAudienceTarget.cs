namespace HabloTruckPlatform.Application.Models;

public sealed record ExternalAudienceTarget(
    string Provider,
    string ExternalSubject,
    string Channel,
    bool IsPrimary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc
);
