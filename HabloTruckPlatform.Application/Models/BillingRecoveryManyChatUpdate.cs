namespace HabloTruckPlatform.Application.Models;

public static class BillingRecoveryManyChatStatuses
{
    public const string RecoveryActive = "recovery_active";
    public const string PaymentUpdatePendingConfirmation = "payment_update_pending_confirmation";
    public const string PaymentRetryFailed = "payment_retry_failed";
    public const string Recovered = "recovered";
    public const string Closed = "closed";
}

public sealed record BillingRecoveryManyChatUpdate(
    string SubscriberId,
    string UserId,
    string? CompanyId,
    string? SubscriptionId,
    string Status,
    DateTimeOffset StatusAtUtc,
    DateTimeOffset? RecoveryStartedAtUtc,
    string? InvoiceId,
    string? InvoiceStatus,
    bool ActionRequired,
    bool Recovered);
