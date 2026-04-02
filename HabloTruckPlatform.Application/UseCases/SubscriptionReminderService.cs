using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class SubscriptionReminderService
{
    private const string OperationName = "subscription_reminder_daily";

    private readonly IUserStore _users;
    private readonly ICompanyStore _companies;
    private readonly ISubscriptionReminderStore _reminders;
    private readonly IManyChatSync _manyChat;
    private readonly IManyChatDispatchQueue? _manyChatDispatchQueue;
    private readonly IFailedActionStore _failedActionStore;
    private readonly IJobCheckpointStore _jobCheckpoints;
    private readonly IClock _clock;
    private readonly IAppMetrics? _metrics;
    private readonly ILogger<SubscriptionReminderService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public SubscriptionReminderService(
        IUserStore users,
        ICompanyStore companies,
        ISubscriptionReminderStore reminders,
        IManyChatSync manyChat,
        IFailedActionStore failedActionStore,
        IClock clock,
        ILogger<SubscriptionReminderService> logger,
        IJobCheckpointStore? jobCheckpoints = null,
        IManyChatDispatchQueue? manyChatDispatchQueue = null,
        IAppMetrics? metrics = null)
    {
        _users = users;
        _companies = companies;
        _reminders = reminders;
        _manyChat = manyChat;
        _manyChatDispatchQueue = manyChatDispatchQueue;
        _failedActionStore = failedActionStore;
        _jobCheckpoints = jobCheckpoints ?? new NoopJobCheckpointStore();
        _clock = clock;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task RunDailyAsync(int take = 2000, CancellationToken ct = default)
    {
        if (take <= 0)
            throw new ArgumentOutOfRangeException(nameof(take));

        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} Take={Take}",
            "entry",
            OperationName,
            take);

        var checkpointKey = $"{OperationName}:users_with_stripe";
        var startBucket = await ReadStartBucketAsync(checkpointKey, ct);
        var queryWatch = Stopwatch.StartNew();
        var nowUtc = _clock.UtcNow;
        var page = await _users.QueryUsersWithStripePageAsync(take, startBucket, ct);
        await _jobCheckpoints.UpsertCursorAsync(checkpointKey, page.NextBucket.ToString(), ct);
        var users = page.Users;

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} UserCount={UserCount} StartBucket={StartBucket} NextBucket={NextBucket} BucketsScanned={BucketsScanned}",
            "persistence",
            "users.query_with_stripe",
            "Users",
            queryWatch.ElapsedMilliseconds,
            users.Count,
            page.StartBucket,
            page.NextBucket,
            page.BucketsScanned);

        var scanned = 0;
        var due = 0;
        var sent = 0;
        var skippedDuplicate = 0;

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            using var userScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["OperationName"] = OperationName,
                ["UserId"] = user.UserId,
                ["CompanyId"] = user.CompanyId,
                ["StripeCustomerId"] = user.StripeCustomerId,
                ["SubscriptionId"] = user.StripeSubscriptionId
            });

            var dispatch = await BuildDispatchAsync(user, nowUtc, ct);
            if (dispatch is null)
                continue;

            due++;

            // Idempotency is enforced at the reminder-window level (for example 7d/3d/1d),
            // so journey flips (auto-renew <-> cancel-scheduled) cannot double-send
            // for the same subscription and billing period.
            var windowKey = BuildReminderWindowKey(dispatch);
            var idempotencyAnchorUtc = dispatch.JourneyAnchorUtc ?? dispatch.PeriodEndUtc;
            var reminderId = $"{dispatch.SubscriptionId}:{windowKey}:{idempotencyAnchorUtc:yyyyMMdd}";
            var payload = JsonSerializer.Serialize(
                new ManyChatSubscriptionReminderFailedActionPayload(
                    Dispatch: dispatch,
                    ReminderId: reminderId,
                    CorrelationId: dispatch.UserId,
                    Reason: "send_subscription_reminder",
                    OperationName: "manychat_send_subscription_reminder"),
                JsonOpts);

            using var reminderScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["ReminderId"] = reminderId
            });

            var idempotencyWatch = Stopwatch.StartNew();
            var firstTime = await _reminders.TryMarkSentAsync(
                dispatch.SubscriptionId,
                windowKey,
                idempotencyAnchorUtc,
                nowUtc,
                ct);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success} FirstTime={FirstTime}",
                "persistence",
                "subscription_reminder.try_mark_sent",
                "SubscriptionReminders",
                idempotencyWatch.ElapsedMilliseconds,
                true,
                firstTime);

            if (!firstTime)
            {
                skippedDuplicate++;

                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} ReminderType={ReminderType}",
                    "decision",
                    "reminder_dispatch",
                    "skipped_duplicate",
                    "reminder_window_already_sent",
                    dispatch.ReminderType);
                continue;
            }

            var dependencyWatch = Stopwatch.StartNew();
            try
            {
                if (_manyChatDispatchQueue is not null)
                {
                    await _manyChatDispatchQueue.EnqueueAsync(
                        new ManyChatDispatchMessage(
                            FailedActionRetryService.ActionManyChatSubscriptionReminder,
                            payload,
                            dispatch.UserId,
                            _clock.UtcNow),
                        ct);
                    _metrics?.ManyChatDispatchQueued(FailedActionRetryService.ActionManyChatSubscriptionReminder);
                }
                else
                {
                    await _manyChat.SendSubscriptionReminderAsync(dispatch, ct);
                }

                sent++;

                _logger.LogInformation(
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ReminderType={ReminderType} Journey={Journey} DispatchMode={DispatchMode} DurationMs={DurationMs}",
                    "outcome",
                    _manyChatDispatchQueue is null ? "reminder_sent" : "reminder_queued",
                    _manyChatDispatchQueue is null ? "manychat_flow_triggered" : "manychat_dispatch_enqueued",
                    dispatch.ReminderType,
                    dispatch.Journey,
                    _manyChatDispatchQueue is null ? "direct" : "queue",
                    dependencyWatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ManyChatRequestException ex) when (ex.IsRetryable)
            {
                _logger.LogWarning(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} ReminderType={ReminderType} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "exception",
                    "dependency_failed",
                    "manychat",
                    "send_subscription_reminder",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds,
                    dispatch.ReminderType,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);

                await _failedActionStore.EnqueueAsync(
                    FailedActionRetryService.ActionManyChatSubscriptionReminder,
                    payload,
                    _clock.UtcNow.AddMinutes(2),
                    ct);

                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} ReminderType={ReminderType}",
                    "decision",
                    "manychat_send_subscription_reminder",
                    "queued_for_retry",
                    "retryable_manychat_failure",
                    dispatch.ReminderType);
            }
            catch (ManyChatRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} ReminderType={ReminderType} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "exception",
                    "validation_failed",
                    "manychat",
                    "send_subscription_reminder",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds,
                    dispatch.ReminderType,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} ReminderType={ReminderType}",
                    "exception",
                    "dependency_failed",
                    "manychat",
                    "send_subscription_reminder",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds,
                    dispatch.ReminderType);

                if (_manyChatDispatchQueue is not null)
                {
                    try
                    {
                        await _failedActionStore.EnqueueAsync(
                            FailedActionRetryService.ActionManyChatSubscriptionReminder,
                            payload,
                            _clock.UtcNow.AddMinutes(2),
                            ct);
                    }
                    catch (Exception enqueueEx)
                    {
                        _logger.LogError(
                            enqueueEx,
                            "Persistence failed. LogCategory={LogCategory} Outcome={Outcome} PersistenceOperation={PersistenceOperation} Target={Target} ReminderType={ReminderType}",
                            "exception",
                            "dependency_failed",
                            "failed_action.enqueue",
                            "FailedActions",
                            dispatch.ReminderType);
                    }
                }
            }
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Scanned={Scanned} Due={Due} Sent={Sent} SkippedDuplicate={SkippedDuplicate} DurationMs={DurationMs}",
            "outcome",
            "completed",
            scanned,
            due,
            sent,
            skippedDuplicate,
            opWatch.ElapsedMilliseconds);
    }

    private async Task<int> ReadStartBucketAsync(string checkpointKey, CancellationToken ct)
    {
        var raw = await _jobCheckpoints.GetCursorAsync(checkpointKey, ct);
        return int.TryParse(raw, out var bucket) ? bucket : 0;
    }

    private sealed class NoopJobCheckpointStore : IJobCheckpointStore
    {
        public Task<string?> GetCursorAsync(string jobName, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task UpsertCursorAsync(string jobName, string cursor, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static string BuildReminderWindowKey(SubscriptionReminderDispatch dispatch)
    {
        if (string.Equals(dispatch.Journey, "payment_recovery", StringComparison.OrdinalIgnoreCase))
        {
            var day = Math.Max(0, dispatch.JourneyDay ?? 0);
            return $"payment_recovery_day_{day}";
        }

        var days = Math.Max(0, dispatch.DaysUntilPeriodEnd);
        return $"window_{days}d";
    }

    private async Task<SubscriptionReminderDispatch?> BuildDispatchAsync(
        User user,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "build_dispatch",
                "no_action_needed",
                "missing_manychat_subscriber_id");
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "build_dispatch",
                "no_action_needed",
                "missing_subscription_id");
            return null;
        }

        var paymentRecoveryDispatch = BuildPaymentRecoveryDispatch(user, nowUtc);
        if (paymentRecoveryDispatch is not null)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} ReminderType={ReminderType} Journey={Journey}",
                "decision",
                "build_dispatch",
                "applied",
                "payment_recovery_followup_ready",
                paymentRecoveryDispatch.ReminderType,
                paymentRecoveryDispatch.Journey);
            return paymentRecoveryDispatch;
        }

        var facts = new SubscriptionReminderFacts(
            SubscriptionId: user.StripeSubscriptionId,
            Status: user.SubscriptionStatus,
            PlanTerm: user.IndividualPlanTerm,
            CancelAtPeriodEnd: user.StripeCancelAtPeriodEnd,
            CurrentPeriodEndUtc: user.StripeCurrentPeriodEndUtc,
            PlanType: user.PlanType);

        var decision = SubscriptionReminderEvaluator.Evaluate(facts, nowUtc);
        if (decision is null)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "build_dispatch",
                "no_action_needed",
                "evaluator_returned_null");
            return null;
        }

        var isCompanyReminder = IsCompanyPlan(user.PlanType);
        string? companyId = null;

        if (isCompanyReminder)
        {
            var company = await ResolveCompanyAsync(user, ct);
            if (company is null)
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "build_dispatch",
                    "no_action_needed",
                    "company_not_resolved_for_company_reminder");
                return null;
            }

            if (!IsAdminTarget(user, company))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "build_dispatch",
                    "denied",
                    "user_not_company_admin_target");
                return null;
            }

            companyId = company.CompanyId;
        }

        var segment = DeriveAudienceSegment(user, nowUtc);

        _logger.LogInformation(
            "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} ReminderType={ReminderType} Journey={Journey} DaysUntilPeriodEnd={DaysUntilPeriodEnd} CompanyReminder={CompanyReminder}",
            "decision",
            "build_dispatch",
            "applied",
            "dispatch_ready",
            decision.Kind.ToEventName(),
            decision.Journey,
            decision.DaysUntilPeriodEnd,
            isCompanyReminder);

        return new SubscriptionReminderDispatch(
            SubscriberId: user.ManyChatSubscriberId!.Trim(),
            UserId: user.UserId,
            SubscriptionId: user.StripeSubscriptionId!.Trim(),
            ReminderType: decision.Kind.ToEventName(),
            Journey: decision.Journey == SubscriptionReminderJourney.SaveBeforeChurn ? "save_before_churn" : "auto_renew",
            DaysUntilPeriodEnd: decision.DaysUntilPeriodEnd,
            PeriodEndUtc: decision.PeriodEndUtc,
            UsePositiveContinuityFraming: decision.UsePositiveContinuityFraming,
            ReminderTone: decision.Tone.ToString(),
            TemplateKey: decision.TemplateKey,
            AudienceSegment: segment,
            IsCompanyReminder: isCompanyReminder,
            CompanyId: companyId,
            PlanTerm: user.IndividualPlanTerm
        );
    }

    private SubscriptionReminderDispatch? BuildPaymentRecoveryDispatch(User user, DateTimeOffset nowUtc)
    {
        if (!IsPaymentRecoveryJourneyActive(user))
            return null;

        if (user.PaymentRecoveryStartedAtUtc is null)
            return null;

        var recoveryDay = (nowUtc.UtcDateTime.Date - user.PaymentRecoveryStartedAtUtc.Value.UtcDateTime.Date).Days;
        if (recoveryDay < 1)
            return null;

        var periodEndUtc = user.StripeCurrentPeriodEndUtc ?? user.PaymentRecoveryStartedAtUtc.Value;
        var daysUntilPeriodEnd = Math.Max(0, (periodEndUtc.UtcDateTime.Date - nowUtc.UtcDateTime.Date).Days);
        var segment = DeriveAudienceSegment(user, nowUtc);

        return new SubscriptionReminderDispatch(
            SubscriberId: user.ManyChatSubscriberId!.Trim(),
            UserId: user.UserId,
            SubscriptionId: user.StripeSubscriptionId!.Trim(),
            ReminderType: $"payment_recovery_followup_day_{recoveryDay}",
            Journey: "payment_recovery",
            DaysUntilPeriodEnd: daysUntilPeriodEnd,
            PeriodEndUtc: periodEndUtc,
            UsePositiveContinuityFraming: false,
            ReminderTone: "PaymentRecoveryUpdateMethod",
            TemplateKey: "payment_recovery_followup",
            AudienceSegment: segment,
            IsCompanyReminder: false,
            CompanyId: user.CompanyId,
            PlanTerm: user.IndividualPlanTerm,
            JourneyDay: recoveryDay,
            JourneyAnchorUtc: user.PaymentRecoveryStartedAtUtc);
    }

    private async Task<Company?> ResolveCompanyAsync(User user, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(user.CompanyId))
        {
            var company = await _companies.GetAsync(user.CompanyId!, ct);
            if (company is not null)
                return company;
        }

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
            return await _companies.GetByStripeCustomerIdAsync(user.StripeCustomerId!, ct);

        return null;
    }

    private static bool IsAdminTarget(User user, Company company)
    {
        if (string.IsNullOrWhiteSpace(company.AdminEmailNormalized))
            return true; // fallback to the current subscription owner user

        if (string.IsNullOrWhiteSpace(user.EmailNormalized))
            return false;

        return string.Equals(
            user.EmailNormalized,
            company.AdminEmailNormalized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanyPlan(string? planType)
    {
        var plan = string.IsNullOrWhiteSpace(planType) ? null : planType.Trim().ToLowerInvariant();
        return plan is "company_seat" or "fleet" or "fleet_seat";
    }

    private static string DeriveAudienceSegment(User user, DateTimeOffset nowUtc)
    {
        // Usage telemetry is not currently persisted in user projections.
        // We use the latest known engagement proxy as a lightweight segment hook.
        var markerUtc = user.LastManyChatSyncAtUtc ?? user.UpdatedAtUtc;
        if (markerUtc is null)
            return "at_risk";

        var activeWindow = nowUtc.AddDays(-30);
        return markerUtc.Value >= activeWindow ? "active" : "at_risk";
    }

    private static bool IsPaymentRecoveryJourneyActive(User user)
    {
        if (user.PaymentRecoveryStartedAtUtc is null)
            return false;

        var status = string.IsNullOrWhiteSpace(user.SubscriptionStatus)
            ? null
            : user.SubscriptionStatus.Trim().ToLowerInvariant();

        return status is "past_due" or "payment_failed" or "unpaid" or "incomplete" or "incomplete_expired";
    }
}
