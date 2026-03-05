public sealed record StripeSubscriptionFacts(
    string? CustomerId,
    string? SubscriptionId,
    string? Status,
    string? PriceId,
    string? PlanTerm, // "monthly" | "annual" | null (derived in App layer)
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc);