using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class ManyChatDispatchQueueTests
{
    [Fact]
    public async Task AccessOrchestrator_Should_QueueAccessSync_WhenDispatchQueueIsConfigured()
    {
        var now = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var user = new User
        {
            UserId = "U_queue_sync",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_queue_sync"
        };
        userStore.Add(user);

        var manyChat = new RecordingManyChatSync();
        var queue = new RecordingManyChatDispatchQueue();
        var sut = new AccessOrchestrator(
            userStore,
            new InMemorySeatStore(),
            new InMemoryEntitlementStore(),
            manyChat,
            new InMemoryFailedActionStore(),
            new FixedClock(now),
            new CompanyGracePolicy(7),
            NullLogger<AccessOrchestrator>.Instance,
            queue);

        await sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(0, manyChat.SyncCalls);
        var message = Assert.Single(queue.Messages);
        Assert.Equal(FailedActionRetryService.ActionManyChatSync, message.ActionType);

        using var payload = JsonDocument.Parse(message.PayloadJson);
        Assert.Equal(user.UserId, payload.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task SubscriptionReminderService_Should_QueueReminder_WhenDispatchQueueIsConfigured()
    {
        var now = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        var users = new InMemoryUserStore();
        users.Add(new User
        {
            UserId = "U_queue_reminder",
            ManyChatSubscriberId = "sid_queue_reminder",
            StripeSubscriptionId = "sub_queue_reminder",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            PlanType = "individual_monthly"
        });

        var manyChat = new RecordingManyChatSync();
        var queue = new RecordingManyChatDispatchQueue();
        var sut = new SubscriptionReminderService(
            users,
            new InMemoryCompanyStore(),
            new InMemoryReminderStore(),
            manyChat,
            new InMemoryFailedActionStore(),
            new FixedClock(now),
            NullLogger<SubscriptionReminderService>.Instance,
            null,
            queue);

        await sut.RunDailyAsync(take: 50);

        Assert.Empty(manyChat.ReminderDispatches);
        var message = Assert.Single(queue.Messages);
        Assert.Equal(FailedActionRetryService.ActionManyChatSubscriptionReminder, message.ActionType);

        using var payload = JsonDocument.Parse(message.PayloadJson);
        var dispatch = payload.RootElement.GetProperty("dispatch");
        Assert.Equal("U_queue_reminder", dispatch.GetProperty("userId").GetString());
        Assert.Equal("renewal_reminder_7d", dispatch.GetProperty("reminderType").GetString());
    }

    [Fact]
    public async Task QueueProcessor_Should_DispatchQueuedAccessSync_AndUpdateWatermark()
    {
        var now = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        var users = new InMemoryUserStore();
        users.Add(new User
        {
            UserId = "U_process_sync",
            ManyChatSubscriberId = "sid_process_sync",
            EffectiveAccess = new AccessSnapshot(AccessMode.Full, AccessSource.Individual, null),
            UpdatedAtUtc = now
        });

        var queue = new RecordingManyChatDispatchQueue();
        await queue.EnqueueAsync(new ManyChatDispatchMessage(
            FailedActionRetryService.ActionManyChatSync,
            JsonSerializer.Serialize(new ManyChatSyncFailedActionPayload(
                UserPk: Buckets.UserBucketPk("U_process_sync"),
                UserId: "U_process_sync",
                SubscriberId: "sid_process_sync",
                CompanyId: null,
                CorrelationId: "U_process_sync",
                Reason: "sync_access",
                OperationName: "manychat_sync_user_access")),
            "U_process_sync",
            now));

        var manyChat = new RecordingManyChatSync();
        var failedActions = new InMemoryFailedActionStore();
        var retryService = new FailedActionRetryService(
            failedActions,
            users,
            manyChat,
            NullLogger<FailedActionRetryService>.Instance);
        var processor = new ManyChatDispatchQueueProcessorService(
            queue,
            failedActions,
            retryService,
            new FixedClock(now),
            NullLogger<ManyChatDispatchQueueProcessorService>.Instance);

        await processor.RunBatchAsync(maxMessages: 10);

        Assert.Equal(1, manyChat.SyncCalls);
        Assert.Single(queue.CompletedMessageIds);
        Assert.Empty(failedActions.Enqueued);
        var user = users.Get("U_process_sync")!;
        Assert.Equal(nameof(AccessMode.Full), user.LastSyncedAccessMode);
        Assert.Equal((int)AccessSource.Individual, user.LastSyncedAccessSource);
        Assert.NotNull(user.LastManyChatSyncAtUtc);
    }

    [Fact]
    public async Task QueueProcessor_Should_MoveRetryableFailure_ToFailedActions()
    {
        var now = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        var users = new InMemoryUserStore();
        users.Add(new User
        {
            UserId = "U_retry_sync",
            ManyChatSubscriberId = "sid_retry_sync",
            EffectiveAccess = new AccessSnapshot(AccessMode.Full, AccessSource.Individual, null),
            UpdatedAtUtc = now
        });

        var queue = new RecordingManyChatDispatchQueue();
        await queue.EnqueueAsync(new ManyChatDispatchMessage(
            FailedActionRetryService.ActionManyChatSync,
            JsonSerializer.Serialize(new ManyChatSyncFailedActionPayload(
                UserPk: Buckets.UserBucketPk("U_retry_sync"),
                UserId: "U_retry_sync",
                SubscriberId: "sid_retry_sync",
                CompanyId: null,
                CorrelationId: "U_retry_sync",
                Reason: "sync_access",
                OperationName: "manychat_sync_user_access")),
            "U_retry_sync",
            now));

        var manyChat = new RecordingManyChatSync
        {
            SyncException = new ManyChatRequestException(
                path: "fb/subscriber/addTagByName",
                statusCode: HttpStatusCode.ServiceUnavailable,
                isRetryable: true,
                failureCategory: ManyChatFailureCategory.TransientHttp,
                message: "transient")
        };

        var failedActions = new InMemoryFailedActionStore();
        var retryService = new FailedActionRetryService(
            failedActions,
            users,
            manyChat,
            NullLogger<FailedActionRetryService>.Instance);
        var processor = new ManyChatDispatchQueueProcessorService(
            queue,
            failedActions,
            retryService,
            new FixedClock(now),
            NullLogger<ManyChatDispatchQueueProcessorService>.Instance);

        await processor.RunBatchAsync(maxMessages: 10);

        Assert.Single(queue.CompletedMessageIds);
        var failed = Assert.Single(failedActions.Enqueued);
        Assert.Equal(FailedActionRetryService.ActionManyChatSync, failed.ActionType);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class RecordingManyChatDispatchQueue : IManyChatDispatchQueue
    {
        private readonly Queue<ManyChatDispatchLease> _leases = new();
        private int _messageCounter;

        public List<ManyChatDispatchMessage> Messages { get; } = new();
        public List<string> CompletedMessageIds { get; } = new();

        public Task EnqueueAsync(ManyChatDispatchMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            _messageCounter++;
            _leases.Enqueue(new ManyChatDispatchLease(
                $"msg-{_messageCounter}",
                $"pop-{_messageCounter}",
                1,
                message));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ManyChatDispatchLease>> DequeueAsync(int maxMessages, TimeSpan visibilityTimeout, CancellationToken ct = default)
        {
            var items = new List<ManyChatDispatchLease>();
            while (_leases.Count > 0 && items.Count < maxMessages)
            {
                items.Add(_leases.Dequeue());
            }

            return Task.FromResult<IReadOnlyList<ManyChatDispatchLease>>(items);
        }

        public Task CompleteAsync(ManyChatDispatchLease lease, CancellationToken ct = default)
        {
            CompletedMessageIds.Add(lease.MessageId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public int SyncCalls { get; private set; }
        public List<SubscriptionReminderDispatch> ReminderDispatches { get; } = new();
        public Exception? SyncException { get; set; }

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
        {
            if (SyncException is not null)
                throw SyncException;

            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
        {
            ReminderDispatches.Add(dispatch);
            return Task.CompletedTask;
        }

        public Task SyncBillingRecoveryStatusAsync(BillingRecoveryManyChatUpdate update, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
            => Task.FromResult(new ManyChatResponse { status = "ok" });

        public Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
            => Task.FromResult(new ManyChatResponse { status = "ok" });

        public Task<ManyChatResponse> SetCustomFieldByNameAsync(string subscriberId, string fieldName, string value, CancellationToken ct = default)
            => Task.FromResult(new ManyChatResponse { status = "ok" });
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _users = new();

        public void Add(User user)
            => _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public User? Get(string userId)
            => _users.TryGetValue((Buckets.UserBucketPk(userId), userId), out var user) ? user : null;

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(_users.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
        {
            var users = _users.Values
                .Where(x => !string.IsNullOrWhiteSpace(x.StripeSubscriptionId))
                .Take(take)
                .ToArray();
            return Task.FromResult<IReadOnlyList<User>>(users);
        }

        public Task<StripeUserScanPage> QueryUsersWithStripePageAsync(int take = 500, int startBucket = 0, CancellationToken ct = default)
        {
            var users = _users.Values
                .Where(x => !string.IsNullOrWhiteSpace(x.StripeSubscriptionId))
                .Take(take)
                .ToArray();

            return Task.FromResult(new StripeUserScanPage(users, startBucket, startBucket, 0, false));
        }
    }

    private sealed class InMemorySeatStore : ISeatAssignmentStore
    {
        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult<SeatAssignment?>(null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult<Entitlement?>(null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryCompanyStore : ICompanyStore
    {
        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
            => Task.FromResult<Company?>(null);

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult<Company?>(null);

        public Task UpsertAsync(Company company, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryReminderStore : ISubscriptionReminderStore
    {
        private readonly HashSet<string> _sent = new();

        public Task<bool> TryMarkSentAsync(string subscriptionId, string reminderType, DateTimeOffset periodEndUtc, DateTimeOffset sentAtUtc, CancellationToken ct = default)
        {
            var key = $"{subscriptionId}:{reminderType}:{periodEndUtc:O}";
            return Task.FromResult(_sent.Add(key));
        }
    }

    private sealed class InMemoryFailedActionStore : IFailedActionStore
    {
        public List<(string ActionType, string PayloadJson, DateTimeOffset NextRetryUtc)> Enqueued { get; } = new();

        public Task EnsureTableAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
        {
            Enqueued.Add((actionType, payloadJson, nextRetryUtc));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FailedActionItem>> GetDueAsync(DateTimeOffset nowUtc, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());

        public Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RescheduleAsync(string pk, string rk, int attempts, DateTimeOffset nextRetryUtc, string lastError, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MarkDeadAsync(string pk, string rk, int attempts, string lastError, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(DateTimeOffset nowUtc, string status, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());

        public Task RequeueAsync(string pk, string rk, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
