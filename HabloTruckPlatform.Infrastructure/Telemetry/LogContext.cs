using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Infrastructure.Telemetry;

public static class LogContext
{
    public static class Fields
    {
        public const string InvocationId = "InvocationId";
        public const string CorrelationId = "CorrelationId";
        public const string OperationName = "OperationName";
        public const string StripeEventId = "StripeEventId";
        public const string UserId = "UserId";
        public const string CompanyId = "CompanyId";
        public const string StripeCustomerId = "StripeCustomerId";
        public const string SubscriptionId = "SubscriptionId";
        public const string EntitlementId = "EntitlementId";
        public const string SeatAssignmentId = "SeatAssignmentId";
        public const string InviteCode = "InviteCode";
        public const string ReminderId = "ReminderId";
    }

    public static class Categories
    {
        public const string Entry = "entry";
        public const string Step = "step";
        public const string Decision = "decision";
        public const string Dependency = "dependency";
        public const string Persistence = "persistence";
        public const string Outcome = "outcome";
        public const string Exception = "exception";
    }

    public static class Outcomes
    {
        public const string Completed = "completed";
        public const string Applied = "applied";
        public const string SkippedDuplicate = "skipped_duplicate";
        public const string SkippedOutOfOrder = "skipped_out_of_order";
        public const string SkippedNoCustomer = "skipped_no_customer";
        public const string SkippedUnhandled = "skipped_unhandled";
        public const string ValidationFailed = "validation_failed";
        public const string DependencyFailed = "dependency_failed";
        public const string PersistenceFailed = "persistence_failed";
        public const string NoActionNeeded = "no_action_needed";
        public const string Denied = "denied";
        public const string Expired = "expired";
        public const string ReminderSent = "reminder_sent";
        public const string ReminderSkipped = "reminder_skipped";
    }

    public static string ResolveCorrelationId(string? incomingCorrelationId, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(incomingCorrelationId))
            return incomingCorrelationId.Trim();

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();

        return $"corr_{Guid.NewGuid():N}";
    }

    public static string? NormalizeOutcomeAlias(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
            return outcome;

        var normalized = outcome.Trim().ToLowerInvariant();

        return normalized switch
        {
            "ignored_duplicate" => Outcomes.SkippedDuplicate,
            "ignored_out_of_order" => Outcomes.SkippedOutOfOrder,
            "ignored_no_customer" => Outcomes.SkippedNoCustomer,
            "ignored_unhandled" => Outcomes.SkippedUnhandled,
            _ => normalized
        };
    }

    public static IDisposable? BeginOperationScope(
        ILogger logger,
        string operationName,
        string correlationId,
        string? invocationId = null,
        string? stripeEventId = null,
        string? userId = null,
        string? companyId = null,
        string? stripeCustomerId = null,
        string? subscriptionId = null,
        string? entitlementId = null,
        string? seatAssignmentId = null,
        string? inviteCode = null,
        string? reminderId = null)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            [Fields.OperationName] = operationName,
            [Fields.CorrelationId] = correlationId,
            [Fields.InvocationId] = invocationId,
            [Fields.StripeEventId] = stripeEventId,
            [Fields.UserId] = userId,
            [Fields.CompanyId] = companyId,
            [Fields.StripeCustomerId] = stripeCustomerId,
            [Fields.SubscriptionId] = subscriptionId,
            [Fields.EntitlementId] = entitlementId,
            [Fields.SeatAssignmentId] = seatAssignmentId,
            [Fields.InviteCode] = inviteCode,
            [Fields.ReminderId] = reminderId
        });
    }

    public static IDisposable? BeginUserScope(
        ILogger logger,
        string? userId = null,
        string? companyId = null,
        string? stripeCustomerId = null)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            [Fields.UserId] = userId,
            [Fields.CompanyId] = companyId,
            [Fields.StripeCustomerId] = stripeCustomerId
        });
    }
}

