using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class FailedActionRetryService
{
    public const string ActionManyChatSync = FailedActionTypes.ManyChatSync;
    public const string ActionManyChatPaymentFailedFlow = FailedActionTypes.ManyChatPaymentFailedFlow;
    public const string ActionManyChatSubscriptionReminder = FailedActionTypes.ManyChatSubscriptionReminder;
    public const string ActionManyChatBillingRecoveryState = FailedActionTypes.ManyChatBillingRecoveryState;
    // Metadata sync operations (tags/custom fields) are effectively convergent, so they can tolerate
    // a higher retry budget than user-facing flow sends.
    private const int MaxAttemptsManyChatSync = 10;
    // SendFlow actions are user-facing and effectively at-least-once; ambiguous delivery conditions
    // (for example timeout after remote accept) can duplicate messages on retry, so keep cap lower.
    private const int MaxAttemptsManyChatSendFlow = 6;

    private readonly IFailedActionStore _store;
    private readonly IUserStore _users;
    private readonly IManyChatSync _manyChat;
    private readonly ILogger<FailedActionRetryService> _logger;

    public FailedActionRetryService(
        IFailedActionStore store,
        IUserStore users,
        IManyChatSync manyChat,
        ILogger<FailedActionRetryService>? logger = null)
    {
        _store = store;
        _users = users;
        _manyChat = manyChat;
        _logger = logger ?? NullLogger<FailedActionRetryService>.Instance;
    }

    public async Task RetryDueAsync(int lookbackHours, int take, CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} LookbackHours={LookbackHours} Take={Take}",
            "entry",
            "failed_action_retry",
            lookbackHours,
            take);

        var queryWatch = Stopwatch.StartNew();
        var due = await _store.GetDueAsync(now, lookbackHours: lookbackHours, take: take, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} DueCount={DueCount}",
            "persistence",
            "failed_actions.get_due",
            "FailedActions",
            queryWatch.ElapsedMilliseconds,
            due.Count);

        var succeeded = 0;
        var rescheduled = 0;
        var dead = 0;

        foreach (var item in due)
        {
            var maxAttempts = ResolveMaxAttempts(item.ActionType);

            using var itemScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["OperationName"] = "failed_action_retry_item",
                ["CorrelationId"] = item.Pk,
                ["UserId"] = null,
                ["ReminderId"] = null,
                ["SubscriptionId"] = null
            });

            try
            {
                await DispatchAsync(item, ct);
                await _store.MarkSucceededAsync(item.Pk, item.Rk, ct);
                succeeded++;

                _logger.LogInformation(
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts}",
                    "outcome",
                    "completed",
                    "failed_action_retried_successfully",
                    item.ActionType,
                    item.Attempts);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ManyChatRequestException ex) when (!ex.IsRetryable)
            {
                var nextAttempts = item.Attempts + 1;

                await _store.MarkDeadAsync(item.Pk, item.Rk, nextAttempts, ex.Message, ct);
                dead++;

                _logger.LogError(
                    ex,
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "exception",
                    "validation_failed",
                    "failed_action_non_retryable_marked_dead",
                    item.ActionType,
                    nextAttempts,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);
            }
            catch (Exception ex)
            {
                var nextAttempts = item.Attempts + 1;

                if (nextAttempts >= maxAttempts)
                {
                    await _store.MarkDeadAsync(item.Pk, item.Rk, nextAttempts, ex.Message, ct);
                    dead++;

                    _logger.LogError(
                        ex,
                        "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts}",
                        "exception",
                        "dependency_failed",
                        "failed_action_marked_dead",
                        item.ActionType,
                        nextAttempts);
                    continue;
                }

                var nextRetryUtc = ComputeBackoffUtc(now, nextAttempts);
                await _store.RescheduleAsync(item.Pk, item.Rk, nextAttempts, nextRetryUtc, ex.Message, ct);
                rescheduled++;

                _logger.LogWarning(
                    ex,
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts} NextRetryUtc={NextRetryUtc}",
                    "exception",
                    "dependency_failed",
                    "failed_action_rescheduled",
                    item.ActionType,
                    nextAttempts,
                    nextRetryUtc);
            }
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Succeeded={Succeeded} Rescheduled={Rescheduled} Dead={Dead} DurationMs={DurationMs}",
            "outcome",
            "completed",
            succeeded,
            rescheduled,
            dead,
            opWatch.ElapsedMilliseconds);
    }

    public Task DispatchAsync(string actionType, string payloadJson, CancellationToken ct = default)
        => DispatchCoreAsync(
            new FailedActionItem(
                Pk: "dispatch",
                Rk: "dispatch",
                ActionType: actionType,
                PayloadJson: payloadJson,
                Attempts: 0,
                NextRetryUtc: DateTimeOffset.UtcNow),
            ct);

    private Task DispatchAsync(FailedActionItem item, CancellationToken ct)
        => DispatchCoreAsync(item, ct);

    private async Task DispatchCoreAsync(FailedActionItem item, CancellationToken ct)
    {
        if (item.ActionType == ActionManyChatSync)
        {
            var p = JsonSerializer.Deserialize<ManyChatSyncFailedActionPayload>(item.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Invalid payload");

            if (string.IsNullOrWhiteSpace(p.UserPk) || string.IsNullOrWhiteSpace(p.UserId))
                throw new InvalidOperationException("Invalid payload: userPk/userId required.");

            var user = await _users.GetAsync(p.UserPk, p.UserId, ct)
                       ?? throw new InvalidOperationException("User not found for retry");

            var targetSubscriberId = string.IsNullOrWhiteSpace(p.SubscriberId)
                ? user.ManyChatSubscriberId?.Trim()
                : p.SubscriberId.Trim();

            if (string.IsNullOrWhiteSpace(targetSubscriberId))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_retry",
                    "no_action_needed",
                    "missing_subscriber_id");
                return;
            }

            // Build decision from stored snapshot (no recompute needed for retry).
            var snap = user.EffectiveAccess;
            if (snap is null)
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_retry",
                    "no_action_needed",
                    "missing_access_snapshot");
                return;
            }

            var decision = new AccessDecision(
                snap.Mode,
                snap.Source,
                snap.GraceEndsAtUtc,
                Reason: "Retry ManyChat sync");

            var dependencyWatch = Stopwatch.StartNew();
            await _manyChat.SyncUserAccessAsync(CloneForSubscriber(user, targetSubscriberId), decision, ct);
            _logger.LogInformation("#Failed_Action_Retry - Sync_User_To_Manychat - Completed");

            user.LastSyncedAccessMode = decision.Mode.ToString();
            user.LastSyncedAccessSource = (int)decision.Source;
            user.LastSyncedGraceEndsAtUtc = decision.GraceEndsAtUtc;
            user.LastManyChatSyncAtUtc = DateTimeOffset.UtcNow;
            await _users.UpsertAsync(user, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "manychat",
                "sync_user_access_retry",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds,
                true);

            return;
        }

        if (item.ActionType == ActionManyChatPaymentFailedFlow)
        {
            var p = JsonSerializer.Deserialize<ManyChatPaymentFailedFlowFailedActionPayload>(item.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Invalid payload");

            if (string.IsNullOrWhiteSpace(p.SubscriberId))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_payment_failed_retry",
                    "no_action_needed",
                    "missing_subscriber_id");
                return;
            }

            if (!await ShouldSendPaymentRecoveryAsync(p.UserId, p.RecoveryStartedAtUtc, ct))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_payment_failed_retry",
                    "no_action_needed",
                    "payment_recovery_no_longer_active");
                return;
            }

            var dependencyWatch = Stopwatch.StartNew();
            await _manyChat.TriggerPaymentFailedFlowAsync(p.SubscriberId, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "manychat",
                "trigger_payment_failed_flow_retry",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds,
                true);

            return;
        }

        if (item.ActionType == ActionManyChatSubscriptionReminder)
        {
            var p = JsonSerializer.Deserialize<ManyChatSubscriptionReminderFailedActionPayload>(item.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Invalid payload");

            if (p.Dispatch is null)
                throw new InvalidOperationException("Invalid payload: dispatch required.");

            if (string.IsNullOrWhiteSpace(p.Dispatch.SubscriberId))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_subscription_reminder_retry",
                    "no_action_needed",
                    "missing_subscriber_id");
                return;
            }

            if (!await ShouldSendSubscriptionReminderAsync(p.Dispatch, ct))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_subscription_reminder_retry",
                    "no_action_needed",
                    "payment_recovery_no_longer_active");
                return;
            }

            var dependencyWatch = Stopwatch.StartNew();
            await _manyChat.SendSubscriptionReminderAsync(p.Dispatch, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "manychat",
                "send_subscription_reminder_retry",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds,
                true);

            return;
        }

        if (item.ActionType == ActionManyChatBillingRecoveryState)
        {
            var p = JsonSerializer.Deserialize<ManyChatBillingRecoveryStateFailedActionPayload>(item.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Invalid payload");

            if (p.Update is null || string.IsNullOrWhiteSpace(p.Update.SubscriberId))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_billing_recovery_retry",
                    "no_action_needed",
                    "missing_update_or_subscriber_id");
                return;
            }

            if (!await ShouldSendBillingRecoveryStatusAsync(p.Update, ct))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_billing_recovery_retry",
                    "no_action_needed",
                    "billing_recovery_state_no_longer_current");
                return;
            }

            var dependencyWatch = Stopwatch.StartNew();
            await _manyChat.SyncBillingRecoveryStatusAsync(p.Update, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "manychat",
                "sync_billing_recovery_status_retry",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds,
                true);

            return;
        }

        throw new InvalidOperationException($"Unknown actionType: {item.ActionType}");
    }

    private static User CloneForSubscriber(User user, string subscriberId)
        => new()
        {
            UserId = user.UserId,
            EmailNormalized = user.EmailNormalized,
            ManyChatSubscriberId = subscriberId,
            PhoneE164 = user.PhoneE164,
            StripeCustomerId = user.StripeCustomerId,
            StripeSubscriptionId = user.StripeSubscriptionId,
            SubscriptionStatus = user.SubscriptionStatus,
            IndividualGraceEndsAtUtc = user.IndividualGraceEndsAtUtc,
            CompanyId = user.CompanyId,
            SeatEntitlementId = user.SeatEntitlementId,
            SeatStatus = user.SeatStatus,
            EffectiveAccess = user.EffectiveAccess,
            LastStripeEventId = user.LastStripeEventId,
            LastStripeEventCreatedUtc = user.LastStripeEventCreatedUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            CurrentGracePk = user.CurrentGracePk,
            CurrentGraceRk = user.CurrentGraceRk,
            StripePriceId = user.StripePriceId,
            IndividualPlanTerm = user.IndividualPlanTerm,
            StripeCurrentPeriodEndUtc = user.StripeCurrentPeriodEndUtc,
            StripeCancelAtPeriodEnd = user.StripeCancelAtPeriodEnd,
            PaymentRecoveryStartedAtUtc = user.PaymentRecoveryStartedAtUtc,
            LastSyncedAccessMode = user.LastSyncedAccessMode,
            LastSyncedAccessSource = user.LastSyncedAccessSource,
            LastSyncedGraceEndsAtUtc = user.LastSyncedGraceEndsAtUtc,
            LastManyChatSyncAtUtc = user.LastManyChatSyncAtUtc,
            PlanType = user.PlanType,
            CohortId = user.CohortId,
            SchoolId = user.SchoolId,
            CohortAccessGrantedAtUtc = user.CohortAccessGrantedAtUtc
        };

    private async Task<bool> ShouldSendSubscriptionReminderAsync(
        SubscriptionReminderDispatch dispatch,
        CancellationToken ct)
    {
        if (string.Equals(dispatch.Journey, "payment_recovery", StringComparison.OrdinalIgnoreCase))
            return await ShouldSendPaymentRecoveryAsync(dispatch.UserId, dispatch.JourneyAnchorUtc, ct);

        if (string.IsNullOrWhiteSpace(dispatch.UserId))
            return true;

        var normalizedUserId = dispatch.UserId.Trim();
        var user = await _users.GetAsync(Buckets.UserBucketPk(normalizedUserId), normalizedUserId, ct);
        if (user is null)
            return false;

        var evaluationNowUtc = dispatch.PeriodEndUtc.AddDays(-Math.Max(0, dispatch.DaysUntilPeriodEnd));
        var decision = SubscriptionReminderEvaluator.Evaluate(
            new SubscriptionReminderFacts(
                SubscriptionId: user.StripeSubscriptionId,
                Status: user.SubscriptionStatus,
                PlanTerm: user.IndividualPlanTerm,
                CancelAtPeriodEnd: user.StripeCancelAtPeriodEnd,
                CurrentPeriodEndUtc: user.StripeCurrentPeriodEndUtc,
                PlanType: user.PlanType),
            evaluationNowUtc);

        if (decision is null)
            return false;

        return string.Equals(dispatch.ReminderType, decision.Kind.ToEventName(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ShouldSendPaymentRecoveryAsync(
        string? userId,
        DateTimeOffset? recoveryStartedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return true;

        var normalizedUserId = userId.Trim();
        var user = await _users.GetAsync(Buckets.UserBucketPk(normalizedUserId), normalizedUserId, ct);
        if (user is null)
            return false;

        if (user.PaymentRecoveryStartedAtUtc is null)
            return false;

        if (recoveryStartedAtUtc is not null && user.PaymentRecoveryStartedAtUtc != recoveryStartedAtUtc)
            return false;

        var status = user.SubscriptionStatus?.Trim().ToLowerInvariant();
        return status is "past_due" or "payment_failed" or "unpaid" or "incomplete" or "incomplete_expired";
    }

    private async Task<bool> ShouldSendBillingRecoveryStatusAsync(
        BillingRecoveryManyChatUpdate update,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(update.UserId))
            return true;

        var normalizedUserId = update.UserId.Trim();
        var user = await _users.GetAsync(Buckets.UserBucketPk(normalizedUserId), normalizedUserId, ct);
        if (user is null)
            return false;

        return update.Status switch
        {
            BillingRecoveryManyChatStatuses.RecoveryActive
                or BillingRecoveryManyChatStatuses.PaymentUpdatePendingConfirmation
                or BillingRecoveryManyChatStatuses.PaymentRetryFailed
                => await ShouldSendPaymentRecoveryAsync(user.UserId, update.RecoveryStartedAtUtc, ct),

            BillingRecoveryManyChatStatuses.Recovered
                or BillingRecoveryManyChatStatuses.Closed
                => user.PaymentRecoveryStartedAtUtc is null,

            _ => true
        };
    }

    private static DateTimeOffset ComputeBackoffUtc(DateTimeOffset now, int attempts)
    {
        // exponential-ish: 1,2,4,8,16,32,60,120,240...
        var minutes = attempts switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 8,
            5 => 16,
            6 => 32,
            7 => 60,
            8 => 120,
            9 => 240,
            _ => 360
        };
        return now.AddMinutes(minutes);
    }

    private static int ResolveMaxAttempts(string actionType)
        => actionType switch
        {
            // User-facing SendFlow retries are intentionally stricter than metadata sync retries.
            ActionManyChatPaymentFailedFlow or ActionManyChatSubscriptionReminder => MaxAttemptsManyChatSendFlow,
            ActionManyChatBillingRecoveryState => MaxAttemptsManyChatSync,
            _ => MaxAttemptsManyChatSync
        };

}
