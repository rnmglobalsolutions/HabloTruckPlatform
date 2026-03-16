namespace HabloTruckPlatform.Application.Models;

public sealed record StripeSubscriptionSnapshot(
    string SubscriptionId,
    string CustomerId,
    string Status,
    string? PriceId,
    string? Interval,
    int? Quantity,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    DateTimeOffset? CanceledAtUtc,
    DateTimeOffset? EndedAtUtc
)
{
    public StripeSubscriptionSnapshot(
        string SubscriptionId,
        string CustomerId,
        string Status,
        string? PriceId,
        string? Interval,
        bool CancelAtPeriodEnd,
        DateTimeOffset? CurrentPeriodEndUtc,
        DateTimeOffset? CanceledAtUtc,
        DateTimeOffset? EndedAtUtc)
        : this(
            SubscriptionId,
            CustomerId,
            Status,
            PriceId,
            Interval,
            null,
            CancelAtPeriodEnd,
            CurrentPeriodEndUtc,
            CanceledAtUtc,
            EndedAtUtc)
    {
    }
}
