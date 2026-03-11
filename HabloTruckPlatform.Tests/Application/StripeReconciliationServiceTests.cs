using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeReconciliationServiceTests
{
    [Fact]
    public async Task RunAsync_Should_ProjectCancelScheduledStripeTruth_AndKeepAccessFullUntilPeriodEnd()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recon_cancel",
            StripeCustomerId = "cus_recon_cancel",
            StripeSubscriptionId = "sub_recon_cancel",
            SubscriptionStatus = "active",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(5)
        };

        fixture.UserStore.Add(user);

        var periodEnd = now.AddDays(10);
        fixture.StripeAdmin.Subscriptions["sub_recon_cancel"] = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_recon_cancel",
            CustomerId: "cus_recon_cancel",
            Status: "active",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: now,
            EndedAtUtc: null);

        await fixture.Service.RunAsync(take: 100);

        var saved = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("active", saved.SubscriptionStatus);
        Assert.True(saved.StripeCancelAtPeriodEnd);
        Assert.Equal(periodEnd, saved.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Full, saved.EffectiveAccess!.Mode);
    }

    [Fact]
    public async Task RunAsync_Should_ProjectDeletedStripeTruth_AndBlockAccess()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recon_deleted",
            StripeCustomerId = "cus_recon_deleted",
            StripeSubscriptionId = "sub_recon_deleted",
            SubscriptionStatus = "active",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = now.AddDays(3),
            IndividualGraceEndsAtUtc = now.AddDays(1)
        };

        fixture.UserStore.Add(user);

        var endedAt = now.AddHours(-1);
        fixture.StripeAdmin.Subscriptions["sub_recon_deleted"] = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_recon_deleted",
            CustomerId: "cus_recon_deleted",
            Status: "deleted",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: now,
            CanceledAtUtc: now.AddDays(-1),
            EndedAtUtc: endedAt);

        await fixture.Service.RunAsync(take: 100);

        var saved = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("deleted", saved.SubscriptionStatus);
        Assert.Equal(AccessMode.Blocked, saved.EffectiveAccess!.Mode);
        Assert.Null(saved.IndividualGraceEndsAtUtc);
    }

    [Fact]
    public async Task RunAsync_Should_FillMissingCurrentPeriodEnd_FromStripeSnapshot()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recon_period_end",
            StripeCustomerId = "cus_recon_period_end",
            StripeSubscriptionId = "sub_recon_period_end",
            SubscriptionStatus = "active",
            StripeCurrentPeriodEndUtc = null,
            StripeCancelAtPeriodEnd = false
        };

        fixture.UserStore.Add(user);

        var expectedPeriodEnd = now.AddDays(30);
        fixture.StripeAdmin.Subscriptions["sub_recon_period_end"] = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_recon_period_end",
            CustomerId: "cus_recon_period_end",
            Status: "active",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: expectedPeriodEnd,
            CanceledAtUtc: null,
            EndedAtUtc: null);

        await fixture.Service.RunAsync(take: 100);

        var saved = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal(expectedPeriodEnd, saved.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Full, saved.EffectiveAccess!.Mode);
    }

    [Fact]
    public async Task RunAsync_Should_NotRegressDeletedProjection_WhenStripeSnapshotLooksActiveOnSameSubscription()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recon_no_regress",
            StripeCustomerId = "cus_recon_no_regress",
            StripeSubscriptionId = "sub_recon_no_regress",
            SubscriptionStatus = "deleted",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = now.AddDays(-1),
            EffectiveAccess = new AccessSnapshot(AccessMode.Blocked, AccessSource.Individual, null)
        };

        fixture.UserStore.Add(user);

        fixture.StripeAdmin.Subscriptions["sub_recon_no_regress"] = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_recon_no_regress",
            CustomerId: "cus_recon_no_regress",
            Status: "active",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null);

        await fixture.Service.RunAsync(take: 100);

        var saved = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("deleted", saved.SubscriptionStatus);
        Assert.True(saved.StripeCancelAtPeriodEnd);
        Assert.Equal(now.AddDays(-1), saved.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Blocked, saved.EffectiveAccess!.Mode);
    }

    [Fact]
    public async Task RunAsync_Should_NotModifyProjection_WhenStripeSnapshotIsMissing()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recon_missing",
            StripeCustomerId = "cus_recon_missing",
            StripeSubscriptionId = "sub_recon_missing",
            SubscriptionStatus = "active",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(14),
            EffectiveAccess = new AccessSnapshot(AccessMode.Full, AccessSource.Individual, null)
        };

        fixture.UserStore.Add(user);

        await fixture.Service.RunAsync(take: 100);

        var saved = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("active", saved.SubscriptionStatus);
        Assert.False(saved.StripeCancelAtPeriodEnd);
        Assert.Equal(now.AddDays(14), saved.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Full, saved.EffectiveAccess!.Mode);
    }

    private static Fixture BuildFixture(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        var userStore = new InMemoryUserStore();
        var stripeAdmin = new FakeStripeAdminClient();
        var graceIndex = new NoopGraceIndexStore();
        var seatStore = new NoopSeatAssignmentStore();
        var entitlementStore = new NoopEntitlementStore();
        var manyChat = new NoopManyChatSync();
        var failedActions = new NoopFailedActionStore();

        var orchestrator = new AccessOrchestrator(
            userStore,
            seatStore,
            entitlementStore,
            manyChat,
            failedActions,
            clock,
            new CompanyGracePolicy(7));

        var service = new StripeReconciliationService(
            userStore,
            stripeAdmin,
            graceIndex,
            orchestrator,
            clock,
            new GracePolicy(72));

        return new Fixture(service, userStore, stripeAdmin);
    }

    private sealed record Fixture(
        StripeReconciliationService Service,
        InMemoryUserStore UserStore,
        FakeStripeAdminClient StripeAdmin);

    private static DateTimeOffset Utc(int y, int m, int d, int h)
        => new(y, m, d, h, 0, 0, TimeSpan.Zero);

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
            => Task.FromResult<IReadOnlyList<User>>(_users.Values.Take(take).ToList());
    }

    private sealed class FakeStripeAdminClient : IStripeAdminClient
    {
        public Dictionary<string, StripeSubscriptionSnapshot> Subscriptions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        {
            Subscriptions.TryGetValue(subscriptionId, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task<StripeEventData?> GetEventDataAsync(string eventId, CancellationToken ct = default)
            => Task.FromResult<StripeEventData?>(null);
    }

    private sealed class NoopGraceIndexStore : IGraceIndexStore
    {
        public Task UpsertAsync(UserRef userRef, DateTimeOffset graceEndsAtUtc, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteForUserAsync(UserRef userRef, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<GraceIndexItem>> QueryExpiredAsync(string gracePk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GraceIndexItem>>(Array.Empty<GraceIndexItem>());

        public Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopSeatAssignmentStore : ISeatAssignmentStore
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

    private sealed class NoopEntitlementStore : IEntitlementStore
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

    private sealed class NoopManyChatSync : IManyChatSync
    {
        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopFailedActionStore : IFailedActionStore
    {
        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
            => Task.CompletedTask;

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

