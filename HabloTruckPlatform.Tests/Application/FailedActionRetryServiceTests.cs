using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class FailedActionRetryServiceTests
{
    [Fact]
    public async Task RetryDueAsync_Should_DispatchReminderAction_AndMarkSucceeded()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync();
        users.Add(new User
        {
            UserId = "U_retry_reminder",
            ManyChatSubscriberId = "sid_retry_reminder",
            StripeSubscriptionId = "sub_retry_reminder",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            PlanType = "individual_monthly"
        });

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_retry_reminder",
            UserId: "U_retry_reminder",
            SubscriptionId: "sub_retry_reminder",
            ReminderType: "renewal_reminder_7d",
            Journey: "auto_renew",
            DaysUntilPeriodEnd: 7,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: true,
            ReminderTone: "PositiveContinuity",
            TemplateKey: "renewal_positive_continuity_7d",
            AudienceSegment: "active",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly");

        var payload = JsonSerializer.Serialize(new ManyChatSubscriptionReminderFailedActionPayload(
            Dispatch: dispatch,
            ReminderId: "sub_retry_reminder:window_7d:20260320",
            CorrelationId: "U_retry_reminder",
            Reason: "send_subscription_reminder",
            OperationName: "manychat_send_subscription_reminder"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk1",
            Rk: "rk1",
            ActionType: FailedActionRetryService.ActionManyChatSubscriptionReminder,
            PayloadJson: payload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Single(manyChat.ReminderDispatches);
        Assert.Single(store.Succeeded);
        Assert.Empty(store.Dead);
        Assert.Empty(store.Rescheduled);
    }

    [Fact]
    public async Task RetryDueAsync_Should_SkipAutoRenewReminder_WhenCurrentStateMovedToCancelScheduled()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync();

        users.Add(new User
        {
            UserId = "U_retry_auto_suppressed",
            ManyChatSubscriberId = "sid_retry_auto_suppressed",
            StripeSubscriptionId = "sub_retry_auto_suppressed",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            PlanType = "individual_monthly"
        });

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_retry_auto_suppressed",
            UserId: "U_retry_auto_suppressed",
            SubscriptionId: "sub_retry_auto_suppressed",
            ReminderType: "renewal_reminder_1d",
            Journey: "auto_renew",
            DaysUntilPeriodEnd: 1,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: true,
            ReminderTone: "PositiveContinuity",
            TemplateKey: "renewal_positive_continuity_1d",
            AudienceSegment: "active",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly");

        var payload = JsonSerializer.Serialize(new ManyChatSubscriptionReminderFailedActionPayload(
            Dispatch: dispatch,
            ReminderId: "sub_retry_auto_suppressed:window_1d:20260320",
            CorrelationId: "U_retry_auto_suppressed",
            Reason: "send_subscription_reminder",
            OperationName: "manychat_send_subscription_reminder"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_retry_auto_suppressed",
            Rk: "rk_retry_auto_suppressed",
            ActionType: FailedActionRetryService.ActionManyChatSubscriptionReminder,
            PayloadJson: payload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(manyChat.ReminderDispatches);
        Assert.Single(store.Succeeded);
        Assert.Empty(store.Dead);
        Assert.Empty(store.Rescheduled);
    }

    [Fact]
    public async Task RetryDueAsync_Should_SkipSaveBeforeChurnReminder_WhenCurrentStateMovedToPaymentRecovery()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync();

        users.Add(new User
        {
            UserId = "U_retry_save_suppressed",
            ManyChatSubscriberId = "sid_retry_save_suppressed",
            StripeSubscriptionId = "sub_retry_save_suppressed",
            SubscriptionStatus = "past_due",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            PaymentRecoveryStartedAtUtc = new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero),
            PlanType = "individual_monthly"
        });

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_retry_save_suppressed",
            UserId: "U_retry_save_suppressed",
            SubscriptionId: "sub_retry_save_suppressed",
            ReminderType: "save_before_churn_1d",
            Journey: "save_before_churn",
            DaysUntilPeriodEnd: 1,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: false,
            ReminderTone: "EndingSoonReactivation",
            TemplateKey: "save_before_churn_ending_soon_1d",
            AudienceSegment: "at_risk",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly");

        var payload = JsonSerializer.Serialize(new ManyChatSubscriptionReminderFailedActionPayload(
            Dispatch: dispatch,
            ReminderId: "sub_retry_save_suppressed:window_1d:20260320",
            CorrelationId: "U_retry_save_suppressed",
            Reason: "send_subscription_reminder",
            OperationName: "manychat_send_subscription_reminder"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_retry_save_suppressed",
            Rk: "rk_retry_save_suppressed",
            ActionType: FailedActionRetryService.ActionManyChatSubscriptionReminder,
            PayloadJson: payload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(manyChat.ReminderDispatches);
        Assert.Single(store.Succeeded);
        Assert.Empty(store.Dead);
        Assert.Empty(store.Rescheduled);
    }

    [Fact]
    public async Task RetryDueAsync_Should_MarkDead_WhenFailureIsNonRetryableManyChatError()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync
        {
            PaymentFailedFlowException = new ManyChatRequestException(
                path: "fb/sending/sendFlow",
                statusCode: HttpStatusCode.BadRequest,
                isRetryable: false,
                failureCategory: ManyChatFailureCategory.PermanentHttp,
                message: "bad_request")
        };
        var recoveryStartedAt = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        users.Add(new User
        {
            UserId = "U_dead",
            ManyChatSubscriberId = "sid_dead",
            SubscriptionStatus = "past_due",
            PaymentRecoveryStartedAtUtc = recoveryStartedAt
        });

        var payload = JsonSerializer.Serialize(new ManyChatPaymentFailedFlowFailedActionPayload(
            SubscriberId: "sid_dead",
            UserId: "U_dead",
            CompanyId: "C_dead",
            SubscriptionId: "sub_dead",
            RecoveryStartedAtUtc: recoveryStartedAt,
            CorrelationId: "evt_dead",
            Reason: "trigger_payment_failed_flow",
            OperationName: "manychat_trigger_payment_failed"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_dead",
            Rk: "rk_dead",
            ActionType: FailedActionRetryService.ActionManyChatPaymentFailedFlow,
            PayloadJson: payload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(store.Succeeded);
        Assert.Empty(store.Rescheduled);
        Assert.Single(store.Dead);
    }

    [Fact]
    public async Task RetryDueAsync_Should_RescheduleWithIncrementedAttempts_WhenRetryableFailureOccurs()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync
        {
            PaymentFailedFlowException = new ManyChatRequestException(
                path: "fb/sending/sendFlow",
                statusCode: HttpStatusCode.ServiceUnavailable,
                isRetryable: true,
                failureCategory: ManyChatFailureCategory.TransientHttp,
                message: "transient")
        };
        var recoveryStartedAt = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        users.Add(new User
        {
            UserId = "U_reschedule",
            ManyChatSubscriberId = "sid_reschedule",
            SubscriptionStatus = "past_due",
            PaymentRecoveryStartedAtUtc = recoveryStartedAt
        });

        var payload = JsonSerializer.Serialize(new ManyChatPaymentFailedFlowFailedActionPayload(
            SubscriberId: "sid_reschedule",
            UserId: "U_reschedule",
            CompanyId: "C_reschedule",
            SubscriptionId: "sub_reschedule",
            RecoveryStartedAtUtc: recoveryStartedAt,
            CorrelationId: "evt_reschedule",
            Reason: "trigger_payment_failed_flow",
            OperationName: "manychat_trigger_payment_failed"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_reschedule",
            Rk: "rk_reschedule",
            ActionType: FailedActionRetryService.ActionManyChatPaymentFailedFlow,
            PayloadJson: payload,
            Attempts: 2,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(store.Succeeded);
        Assert.Empty(store.Dead);
        var rescheduled = Assert.Single(store.Rescheduled);
        Assert.Equal(3, rescheduled.Attempts);
    }

    [Fact]
    public async Task RetryDueAsync_Should_SkipPaymentFailedFlow_WhenRecoveryHasEnded()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync();

        users.Add(new User
        {
            UserId = "U_recovered",
            ManyChatSubscriberId = "sid_recovered",
            SubscriptionStatus = "active",
            PaymentRecoveryStartedAtUtc = null
        });

        var payload = JsonSerializer.Serialize(new ManyChatPaymentFailedFlowFailedActionPayload(
            SubscriberId: "sid_recovered",
            UserId: "U_recovered",
            CompanyId: "C_recovered",
            SubscriptionId: "sub_recovered",
            RecoveryStartedAtUtc: new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeSpan.Zero),
            CorrelationId: "evt_recovered",
            Reason: "trigger_payment_failed_flow",
            OperationName: "manychat_trigger_payment_failed"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_recovered",
            Rk: "rk_recovered",
            ActionType: FailedActionRetryService.ActionManyChatPaymentFailedFlow,
            PayloadJson: payload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(store.Rescheduled);
        Assert.Empty(store.Dead);
        Assert.Single(store.Succeeded);
    }

    [Fact]
    public async Task RetryDueAsync_Should_SkipPaymentRecoveryReminder_WhenRecoveryEpisodeChanges()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync();
        var currentRecoveryStartedAt = new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

        users.Add(new User
        {
            UserId = "U_recovery_reminder_skip",
            ManyChatSubscriberId = "sid_recovery_reminder_skip",
            SubscriptionStatus = "past_due",
            PaymentRecoveryStartedAtUtc = currentRecoveryStartedAt
        });

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_recovery_reminder_skip",
            UserId: "U_recovery_reminder_skip",
            SubscriptionId: "sub_recovery_reminder_skip",
            ReminderType: "payment_recovery_followup_day_1",
            Journey: "payment_recovery",
            DaysUntilPeriodEnd: 0,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: false,
            ReminderTone: "PaymentRecoveryUpdateMethod",
            TemplateKey: "payment_recovery_followup",
            AudienceSegment: "active",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly",
            JourneyDay: 1,
            JourneyAnchorUtc: currentRecoveryStartedAt.AddDays(-1));

        var payload = JsonSerializer.Serialize(new ManyChatSubscriptionReminderFailedActionPayload(
            Dispatch: dispatch,
            ReminderId: "sub_recovery_reminder_skip:payment_recovery_day_1:20260311",
            CorrelationId: "U_recovery_reminder_skip",
            Reason: "send_subscription_reminder",
            OperationName: "manychat_send_subscription_reminder"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_recovery_reminder_skip",
            Rk: "rk_recovery_reminder_skip",
            ActionType: FailedActionRetryService.ActionManyChatSubscriptionReminder,
            PayloadJson: payload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(manyChat.ReminderDispatches);
        Assert.Empty(store.Rescheduled);
        Assert.Empty(store.Dead);
        Assert.Single(store.Succeeded);
    }

    [Fact]
    public async Task RetryDueAsync_Should_UseLowerMaxAttempts_ForUserFacingSendFlow()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync
        {
            ReminderException = new ManyChatRequestException(
                path: "fb/sending/sendFlow",
                statusCode: HttpStatusCode.BadGateway,
                isRetryable: true,
                failureCategory: ManyChatFailureCategory.TransientHttp,
                message: "transient")
        };

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_dead_sendflow",
            UserId: "U_dead_sendflow",
            SubscriptionId: "sub_dead_sendflow",
            ReminderType: "renewal_reminder_1d",
            Journey: "auto_renew",
            DaysUntilPeriodEnd: 1,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: true,
            ReminderTone: "PositiveContinuity",
            TemplateKey: "renewal_positive_continuity_1d",
            AudienceSegment: "active",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly");

        var payload = JsonSerializer.Serialize(new ManyChatSubscriptionReminderFailedActionPayload(
            Dispatch: dispatch,
            ReminderId: "sub_dead_sendflow:window_1d:20260320",
            CorrelationId: "U_dead_sendflow",
            Reason: "send_subscription_reminder",
            OperationName: "manychat_send_subscription_reminder"));

        // User-facing sendflow max attempts is lower than metadata sync.
        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_dead_sendflow",
            Rk: "rk_dead_sendflow",
            ActionType: FailedActionRetryService.ActionManyChatSubscriptionReminder,
            PayloadJson: payload,
            Attempts: 5,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(store.Succeeded);
        Assert.Empty(store.Rescheduled);
        Assert.Single(store.Dead);
    }

    [Fact]
    public async Task RetryDueAsync_Should_KeepHigherMaxAttempts_ForMetadataSync()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync
        {
            SyncException = new ManyChatRequestException(
                path: "fb/subscriber/addTagByName",
                statusCode: HttpStatusCode.ServiceUnavailable,
                isRetryable: true,
                failureCategory: ManyChatFailureCategory.TransientHttp,
                message: "transient")
        };

        var user = new User
        {
            UserId = "U_sync_retryable",
            ManyChatSubscriberId = "sid_sync_retryable",
            EffectiveAccess = new AccessSnapshot(
                Mode: AccessMode.Full,
                Source: AccessSource.Individual,
                GraceEndsAtUtc: null)
        };
        users.Add(user);

        var payload = JsonSerializer.Serialize(new ManyChatSyncFailedActionPayload(
            UserPk: Buckets.UserBucketPk(user.UserId),
            UserId: user.UserId,
            SubscriberId: user.ManyChatSubscriberId,
            CompanyId: null,
            CorrelationId: user.UserId,
            Reason: "sync_access",
            OperationName: "manychat_sync_user_access"));

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_sync_retryable",
            Rk: "rk_sync_retryable",
            ActionType: FailedActionRetryService.ActionManyChatSync,
            PayloadJson: payload,
            Attempts: 5,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Empty(store.Succeeded);
        Assert.Empty(store.Dead);
        var rescheduled = Assert.Single(store.Rescheduled);
        Assert.Equal(6, rescheduled.Attempts);
    }

    [Fact]
    public async Task RetryDueAsync_Should_KeepLegacyManyChatSyncPayloadCompatible()
    {
        var store = new InMemoryFailedActionStore();
        var users = new InMemoryUserStore();
        var manyChat = new RecordingManyChatSync();

        var user = new User
        {
            UserId = "U_sync_legacy",
            ManyChatSubscriberId = "sid_sync_legacy",
            EffectiveAccess = new AccessSnapshot(
                Mode: AccessMode.Full,
                Source: AccessSource.Individual,
                GraceEndsAtUtc: null)
        };
        users.Add(user);

        // Legacy payload shape used before richer ManyChatSyncFailedActionPayload.
        var legacyPayload = JsonSerializer.Serialize(new
        {
            userPk = Buckets.UserBucketPk(user.UserId),
            userId = user.UserId,
            reason = "sync_access"
        });

        store.DueItems.Add(new FailedActionItem(
            Pk: "pk_sync",
            Rk: "rk_sync",
            ActionType: FailedActionRetryService.ActionManyChatSync,
            PayloadJson: legacyPayload,
            Attempts: 0,
            NextRetryUtc: DateTimeOffset.UtcNow));

        var sut = new FailedActionRetryService(store, users, manyChat, NullLogger<FailedActionRetryService>.Instance);

        await sut.RetryDueAsync(lookbackHours: 12, take: 50);

        Assert.Equal(1, manyChat.SyncCalls);
        Assert.Single(store.Succeeded);
        Assert.Empty(store.Dead);
    }

    private sealed class InMemoryFailedActionStore : IFailedActionStore
    {
        public List<FailedActionItem> DueItems { get; } = new();
        public List<(string Pk, string Rk)> Succeeded { get; } = new();
        public List<(string Pk, string Rk, int Attempts, DateTimeOffset NextRetryUtc, string LastError)> Rescheduled { get; } = new();
        public List<(string Pk, string Rk, int Attempts, string LastError)> Dead { get; } = new();

        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<FailedActionItem>> GetDueAsync(DateTimeOffset nowUtc, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(DueItems.Take(take).ToList());

        public Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default)
        {
            Succeeded.Add((pk, rk));
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(string pk, string rk, int attempts, DateTimeOffset nextRetryUtc, string lastError, CancellationToken ct = default)
        {
            Rescheduled.Add((pk, rk, attempts, nextRetryUtc, lastError));
            return Task.CompletedTask;
        }

        public Task MarkDeadAsync(string pk, string rk, int attempts, string lastError, CancellationToken ct = default)
        {
            Dead.Add((pk, rk, attempts, lastError));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(DateTimeOffset nowUtc, string status, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());

        public Task RequeueAsync(string pk, string rk, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string UserId), User> _users = new();

        public void Add(User user)
            => _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(_users.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public int SyncCalls { get; private set; }
        public List<SubscriptionReminderDispatch> ReminderDispatches { get; } = new();
        public Exception? PaymentFailedFlowException { get; set; }
        public Exception? SyncException { get; set; }
        public Exception? ReminderException { get; set; }

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
        {
            if (SyncException is not null)
                throw SyncException;

            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
        {
            if (PaymentFailedFlowException is not null)
                throw PaymentFailedFlowException;

            return Task.CompletedTask;
        }

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
        {
            if (ReminderException is not null)
                throw ReminderException;

            ReminderDispatches.Add(dispatch);
            return Task.CompletedTask;
        }

        public Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
        {
            return Task.FromResult(new ManyChatResponse
            {
                status = "ok",
                message = $"Tag '{tagName}' removed from subscriber '{subscriberId}'."
            });
        }

        public Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
        {
            return Task.FromResult(new ManyChatResponse
            {
                status = "ok",
                message = $"Tag '{tagName}' added to subscriber '{subscriberId}'."
            });
        }

        public Task<ManyChatResponse> SetCustomFieldByNameAsync(string subscriberId, string fieldName, string value, CancellationToken ct = default)
        {
            return Task.FromResult(new ManyChatResponse
            {
                status = "ok",
                message = $"Custom field '{fieldName}' set to '{value}' for subscriber '{subscriberId}'."
            });
        }
    }
}
