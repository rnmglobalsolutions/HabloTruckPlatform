namespace HabloTruckPlatform.Domain.Stripe;

public static class StripeStatusMapper
{
    public static bool IsGraceWorthySubscriptionStatus(string? status)
        => status is not null && (
            status.Equals("past_due", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("unpaid", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("canceled", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("deleted", StringComparison.OrdinalIgnoreCase)
        );

    public static bool IsActive(string? status)
        => status is not null && status.Equals("active", StringComparison.OrdinalIgnoreCase);
}