public enum StripeSignalKind
{
    SubscriptionUpdated,
    SubscriptionDeleted,
    InvoicePaid,
    InvoicePaymentFailed
}

public sealed record StripeSignal(
    StripeSignalKind Kind,
    string StripeEventId,
    DateTimeOffset StripeEventCreatedUtc,
    string StripeCustomerId,
    string? StripeSubscriptionId,
    string? Status,
    string? PriceId,
    string? Interval,                 // "month" / "year" (best-effort)
    int? Quantity,
    bool? CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc
);
