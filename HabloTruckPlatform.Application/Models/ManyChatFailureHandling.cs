using System.Net;

namespace HabloTruckPlatform.Application.Models;

public enum ManyChatFailureCategory
{
    Unknown = 0,
    Transport = 1,
    Timeout = 2,
    TransientHttp = 3,
    PermanentHttp = 4,
    InvalidPayload = 5
}

public sealed class ManyChatRequestException : Exception
{
    public ManyChatRequestException(
        string path,
        HttpStatusCode? statusCode,
        bool isRetryable,
        ManyChatFailureCategory failureCategory,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Path = path;
        StatusCode = statusCode;
        IsRetryable = isRetryable;
        FailureCategory = failureCategory;
    }

    public string Path { get; }
    public HttpStatusCode? StatusCode { get; }
    public bool IsRetryable { get; }
    public ManyChatFailureCategory FailureCategory { get; }
}

public static class FailedActionTypes
{
    public const string ManyChatSync = "manychat.sync";
    public const string ManyChatPaymentFailedFlow = "manychat.payment_failed_flow";
    public const string ManyChatSubscriptionReminder = "manychat.subscription_reminder";
    public const string ManyChatBillingRecoveryState = "manychat.billing_recovery_state";
    public const string ManyChatSubscriptionCancelScheduledLifecycle = "manychat.subscription_cancel_scheduled_lifecycle";
    public const string ManyChatSubscriptionDeletedLifecycle = "manychat.subscription_deleted_lifecycle";
}

public static class ManyChatLifecycleTags
{
    public const string AccessFull = "HT_ACCESS_FULL";
    public const string CancelScheduled = "HT_CANCEL_SCHEDULED";
    public const string Churned = "HT_CHURNED";
}

public sealed record ManyChatSyncFailedActionPayload(
    string? UserPk,
    string? UserId,
    string? SubscriberId,
    string? CompanyId,
    string? CorrelationId,
    string? Reason,
    string? OperationName
);

public sealed record ManyChatPaymentFailedFlowFailedActionPayload(
    string? SubscriberId,
    string? UserId,
    string? CompanyId,
    string? SubscriptionId,
    DateTimeOffset? RecoveryStartedAtUtc,
    string? CorrelationId,
    string? Reason,
    string? OperationName
);

public sealed record ManyChatSubscriptionReminderFailedActionPayload(
    SubscriptionReminderDispatch? Dispatch,
    string? ReminderId,
    string? CorrelationId,
    string? Reason,
    string? OperationName
);

public sealed record ManyChatBillingRecoveryStateFailedActionPayload(
    BillingRecoveryManyChatUpdate? Update,
    string? UserPk,
    string? UserId,
    string? CorrelationId,
    string? Reason,
    string? OperationName
);

public sealed record ManyChatSubscriptionDeletedLifecycleUpdate(
    string? SubscriberId,
    string? UserId,
    string? CompanyId,
    string? SubscriptionId,
    bool AccessBlocked,
    string? CorrelationId
);

public sealed record ManyChatSubscriptionDeletedLifecycleFailedActionPayload(
    ManyChatSubscriptionDeletedLifecycleUpdate? Update,
    string? UserPk,
    string? UserId,
    string? CorrelationId,
    string? Reason,
    string? OperationName
);

public sealed record ManyChatSubscriptionCancelScheduledLifecycleUpdate(
    string? SubscriberId,
    string? UserId,
    string? CompanyId,
    string? SubscriptionId,
    bool CancelScheduled,
    string? CorrelationId
);

public sealed record ManyChatSubscriptionCancelScheduledLifecycleFailedActionPayload(
    ManyChatSubscriptionCancelScheduledLifecycleUpdate? Update,
    string? UserPk,
    string? UserId,
    string? CorrelationId,
    string? Reason,
    string? OperationName
);
