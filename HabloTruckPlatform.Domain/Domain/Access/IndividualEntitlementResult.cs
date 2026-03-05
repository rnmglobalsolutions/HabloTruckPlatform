public sealed record IndividualEntitlementResult(
    IndividualEntitlementState State,
    DateTimeOffset? GraceEndsAtUtc,
    string Reason);