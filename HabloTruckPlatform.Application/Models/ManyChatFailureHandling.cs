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
