namespace HabloTruckPlatform.Application.Models;

public sealed record StripeEventAuditItem(
    string StripeEventId,
    string EventType,
    string? CustomerId,
    string? SubscriptionId,
    string? PriceId,
    string? Status,
    DateTimeOffset EventCreatedUtc,
    DateTimeOffset ProcessedUtc,
    string Outcome,
    string? Reason,
    string? UserPk,
    string? UserId,
    DateTimeOffset? CurrentPeriodEndUtc,
    bool? CancelAtPeriodEnd,
    string? AccessMode,
    string? AccessSource,
    string? Error
);