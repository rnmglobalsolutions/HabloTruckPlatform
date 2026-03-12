using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using System.Net;
using System.Text.Json;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class AccessOrchestratorTests
{
    [Fact]
    public async Task RecomputeForUserAsync_Should_GrantFull_When_IndividualSubscriptionIsActive()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_active", subscriptionStatus: "active", manyChatSubscriberId: "sid_active");
        fixture.UserStore.Add(user);

        var decision = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Full, decision.Mode);
        Assert.Equal(AccessSource.Individual, decision.Source);
        Assert.Equal(AccessMode.Full, fixture.UserStore.Get(user.UserId)!.EffectiveAccess!.Mode);
        Assert.Equal(1, fixture.ManyChat.SyncCalls);
        Assert.Empty(fixture.FailedActionStore.Enqueued);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_GrantGrace_When_IndividualGraceIsActive()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_grace", subscriptionStatus: null, manyChatSubscriberId: "sid_grace");
        user.IndividualGraceEndsAtUtc = now.AddHours(8);
        fixture.UserStore.Add(user);

        var decision = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Grace, decision.Mode);
        Assert.Equal(AccessSource.Individual, decision.Source);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_BeBlocked_When_SubscriptionDeletedAndNoCompanyAccess()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_deleted", subscriptionStatus: "deleted", manyChatSubscriberId: "sid_deleted");
        fixture.UserStore.Add(user);

        var decision = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Blocked, decision.Mode);
        Assert.Equal(AccessSource.None, decision.Source);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_KeepFull_When_CancelScheduledAndStillInPaidWindow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_cancel_scheduled", subscriptionStatus: "active", manyChatSubscriberId: "sid_cancel");
        user.StripeCancelAtPeriodEnd = true;
        user.StripeCurrentPeriodEndUtc = now.AddDays(7);
        fixture.UserStore.Add(user);

        var decision = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Full, decision.Mode);
        Assert.Equal(AccessSource.Individual, decision.Source);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_GrantCompanyAccess_When_SeatAndEntitlementAreActive()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_company_full", subscriptionStatus: null, manyChatSubscriberId: "sid_company_full");
        user.CompanyId = "C1";
        fixture.UserStore.Add(user);

        fixture.SeatStore.Seat = new SeatAssignment
        {
            CompanyId = "C1",
            UserId = user.UserId,
            EntitlementId = "E1",
            Status = "active",
            AssignedAtUtc = now,
            UpdatedAtUtc = now
        };

        fixture.EntitlementStore.Entitlement = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "E1",
            SeatsTotal = 10,
            SeatsUsed = 1,
            Status = "active",
            StartUtc = now.AddDays(-10),
            EndUtc = now.AddDays(15),
            UpdatedAtUtc = now
        };

        var decision = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Full, decision.Mode);
        Assert.Equal(AccessSource.Company, decision.Source);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_RecomputeFromFullToGrace_When_CompanyEntitlementEndDateMovesPastNow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_company_transition", subscriptionStatus: null, manyChatSubscriberId: "sid_company_transition");
        user.CompanyId = "C_TRANSITION";
        fixture.UserStore.Add(user);

        fixture.SeatStore.Seat = new SeatAssignment
        {
            CompanyId = "C_TRANSITION",
            UserId = user.UserId,
            EntitlementId = "E_TRANSITION",
            Status = "active",
            AssignedAtUtc = now,
            UpdatedAtUtc = now
        };

        fixture.EntitlementStore.Entitlement = new Entitlement
        {
            CompanyId = "C_TRANSITION",
            EntitlementId = "E_TRANSITION",
            SeatsTotal = 10,
            SeatsUsed = 1,
            Status = "active",
            StartUtc = now.AddDays(-10),
            EndUtc = now.AddDays(5),
            UpdatedAtUtc = now
        };

        var first = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Full, first.Mode);
        Assert.Equal(AccessSource.Company, first.Source);

        fixture.EntitlementStore.Entitlement = new Entitlement
        {
            CompanyId = "C_TRANSITION",
            EntitlementId = "E_TRANSITION",
            SeatsTotal = 10,
            SeatsUsed = 1,
            Status = "active",
            StartUtc = now.AddDays(-10),
            EndUtc = now.AddMinutes(-1),
            UpdatedAtUtc = now.AddMinutes(1)
        };

        var second = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Grace, second.Mode);
        Assert.Equal(AccessSource.Company, second.Source);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_Deny_When_CompanyEntitlementExpiredBeyondGrace()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_company_expired", subscriptionStatus: null, manyChatSubscriberId: "sid_company_expired");
        user.CompanyId = "C2";
        fixture.UserStore.Add(user);

        fixture.SeatStore.Seat = new SeatAssignment
        {
            CompanyId = "C2",
            UserId = user.UserId,
            EntitlementId = "E2",
            Status = "active",
            AssignedAtUtc = now,
            UpdatedAtUtc = now
        };

        fixture.EntitlementStore.Entitlement = new Entitlement
        {
            CompanyId = "C2",
            EntitlementId = "E2",
            SeatsTotal = 10,
            SeatsUsed = 1,
            Status = "active",
            StartUtc = now.AddDays(-40),
            EndUtc = now.AddDays(-10),
            UpdatedAtUtc = now
        };

        var decision = await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(AccessMode.Blocked, decision.Mode);
        Assert.Equal(AccessSource.None, decision.Source);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_DedupeManyChatSync_WhenDecisionDidNotChange()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_sync_dedupe", subscriptionStatus: "active", manyChatSubscriberId: "sid_sync_dedupe");
        fixture.UserStore.Add(user);

        await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);
        await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Equal(1, fixture.ManyChat.SyncCalls);
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_EnqueueFailedAction_WhenManyChatFailureIsRetryable()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_retryable_sync", subscriptionStatus: "active", manyChatSubscriberId: "sid_retryable_sync");
        fixture.UserStore.Add(user);

        fixture.ManyChat.SyncException = new ManyChatRequestException(
            path: "fb/subscriber/addTagByName",
            statusCode: HttpStatusCode.ServiceUnavailable,
            isRetryable: true,
            failureCategory: ManyChatFailureCategory.TransientHttp,
            message: "transient");

        await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        var queued = Assert.Single(fixture.FailedActionStore.Enqueued);
        Assert.Equal(FailedActionRetryService.ActionManyChatSync, queued.ActionType);

        using var doc = JsonDocument.Parse(queued.PayloadJson);
        Assert.Equal(user.UserId, doc.RootElement.GetProperty("userId").GetString());
        Assert.Equal(user.CompanyId ?? "", doc.RootElement.GetProperty("companyId").GetString() ?? "");
    }

    [Fact]
    public async Task RecomputeForUserAsync_Should_NotEnqueueFailedAction_WhenManyChatFailureIsNonRetryable()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = NewUser("U_non_retryable_sync", subscriptionStatus: "active", manyChatSubscriberId: "sid_non_retryable_sync");
        fixture.UserStore.Add(user);

        fixture.ManyChat.SyncException = new ManyChatRequestException(
            path: "fb/subscriber/addTagByName",
            statusCode: HttpStatusCode.BadRequest,
            isRetryable: false,
            failureCategory: ManyChatFailureCategory.PermanentHttp,
            message: "bad_request");

        await fixture.Sut.RecomputeForUserAsync(user, persistUser: true);

        Assert.Empty(fixture.FailedActionStore.Enqueued);
    }

    private static Fixture BuildFixture(DateTimeOffset now)
    {
        var userStore = new InMemoryUserStore();
        var seatStore = new InMemorySeatStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var manyChat = new RecordingManyChatSync();
        var failedActionStore = new InMemoryFailedActionStore();

        var sut = new AccessOrchestrator(
            userStore,
            seatStore,
            entitlementStore,
            manyChat,
            failedActionStore,
            new FixedClock(now),
            new CompanyGracePolicy(7));

        return new Fixture(sut, userStore, seatStore, entitlementStore, manyChat, failedActionStore);
    }

    private static User NewUser(string userId, string? subscriptionStatus, string? manyChatSubscriberId)
        => new()
        {
            UserId = userId,
            SubscriptionStatus = subscriptionStatus,
            ManyChatSubscriberId = manyChatSubscriberId
        };

    private static DateTimeOffset Utc(int y, int m, int d, int h)
        => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        AccessOrchestrator Sut,
        InMemoryUserStore UserStore,
        InMemorySeatStore SeatStore,
        InMemoryEntitlementStore EntitlementStore,
        RecordingManyChatSync ManyChat,
        InMemoryFailedActionStore FailedActionStore);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _users = new();

        public void Add(User user)
            => _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public User? Get(string userId)
        {
            _users.TryGetValue((Buckets.UserBucketPk(userId), userId), out var user);
            return user;
        }

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
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
    }

    private sealed class InMemorySeatStore : ISeatAssignmentStore
    {
        public SeatAssignment? Seat { get; set; }

        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult(Seat is not null && Seat.CompanyId == companyId && Seat.UserId == userId ? Seat : null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
        {
            Seat = seat;
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        public Entitlement? Entitlement { get; set; }

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(Entitlement is not null && Entitlement.CompanyId == companyId && Entitlement.EntitlementId == entitlementId ? Entitlement : null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public int SyncCalls { get; private set; }
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
            => Task.CompletedTask;
    }

    private sealed class InMemoryFailedActionStore : IFailedActionStore
    {
        public List<FailedActionItem> Enqueued { get; } = new();

        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
        {
            Enqueued.Add(new FailedActionItem("pk", "rk", actionType, payloadJson, 0, nextRetryUtc));
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





