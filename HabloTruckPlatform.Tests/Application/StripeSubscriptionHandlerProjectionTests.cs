using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeSubscriptionHandlerProjectionTests
{
    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_UpdateProjectionFields_ForCancelScheduledSubscription()
    {
        var now = Utc(2026, 3, 10, 12);
        var periodEnd = now.AddDays(14);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_proj_1",
            StripeCustomerId = "cus_proj_1",
            StripeSubscriptionId = "sub_proj_1",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_proj_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_proj_1", user);

        var decision = await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_proj_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_proj_1",
            StripeSubscriptionId: "sub_proj_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: now,
            EndedAtUtc: null));

        var saved = fixture.UserStore.GetById("U_proj_1");

        Assert.NotNull(saved);
        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Full, decision!.Mode);
        Assert.Equal("sub_proj_1", saved!.StripeSubscriptionId);
        Assert.Equal("cus_proj_1", saved.StripeCustomerId);
        Assert.Equal("active", saved.SubscriptionStatus);
        Assert.True(saved.StripeCancelAtPeriodEnd);
        Assert.Equal(periodEnd, saved.StripeCurrentPeriodEndUtc);
        Assert.True(fixture.ManyChatSync.SyncCalls > 0);
    }

    [Fact]
    public async Task HandleInvoicePaymentFailedAsync_Should_OpenGrace_AndTriggerRecoveryFlow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fail_1",
            StripeCustomerId = "cus_fail_1",
            StripeSubscriptionId = "sub_fail_1",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_fail_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fail_1", user);

        var decision = await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fail_1",
            StripeSubscriptionId: "sub_fail_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var saved = fixture.UserStore.GetById("U_fail_1");

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Grace, decision!.Mode);
        Assert.NotNull(saved);
        Assert.Equal("past_due", saved!.SubscriptionStatus);
        Assert.NotNull(saved.IndividualGraceEndsAtUtc);
        Assert.True(saved.IndividualGraceEndsAtUtc > now);
        Assert.Equal(1, fixture.ManyChatSync.PaymentFailedFlowCalls);
    }

    [Fact]
    public async Task HandleInvoicePaidAsync_Should_ClearGrace_AndRestoreFullAccess()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recover_1",
            StripeCustomerId = "cus_recover_1",
            StripeSubscriptionId = "sub_recover_1",
            SubscriptionStatus = "past_due",
            IndividualGraceEndsAtUtc = now.AddHours(10),
            ManyChatSubscriberId = "sid_recover_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_recover_1", user);

        var decision = await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_paid_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_recover_1",
            StripeSubscriptionId: "sub_recover_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var saved = fixture.UserStore.GetById("U_recover_1");

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Full, decision!.Mode);
        Assert.NotNull(saved);
        Assert.Equal("active", saved!.SubscriptionStatus);
        Assert.Null(saved.IndividualGraceEndsAtUtc);
        Assert.Equal(0, fixture.ManyChatSync.PaymentFailedFlowCalls);
    }

    [Fact]
    public async Task HandleSubscriptionDeletedAsync_Should_BlockAndClearGrace_When_EndedNow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_deleted_1",
            StripeCustomerId = "cus_deleted_1",
            StripeSubscriptionId = "sub_deleted_1",
            SubscriptionStatus = "active",
            IndividualGraceEndsAtUtc = now.AddHours(8),
            ManyChatSubscriberId = "sid_deleted_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_deleted_1", user);

        var decision = await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_deleted_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_deleted_1",
            StripeSubscriptionId: "sub_deleted_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now,
            CanceledAtUtc: now,
            EndedAtUtc: now));

        var saved = fixture.UserStore.GetById("U_deleted_1");

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Blocked, decision!.Mode);
        Assert.NotNull(saved);
        Assert.Equal("deleted", saved!.SubscriptionStatus);
        Assert.Null(saved.IndividualGraceEndsAtUtc);
    }

    private static HandlerFixture BuildFixture(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        var userStore = new InMemoryUserStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var userResolver = new InMemoryUserResolver();
        var graceIndexStore = new InMemoryGraceIndexStore();
        var manyChat = new RecordingManyChatSync();
        var failedActions = new NoopFailedActionStore();
        var seatStore = new NoopSeatAssignmentStore();
        var companyStore = new InMemoryCompanyStore();
        var expiryIndexStore = new InMemoryEntitlementExpiryIndexStore();

        var orchestrator = new AccessOrchestrator(
            userStore,
            seatStore,
            entitlementStore,
            manyChat,
            failedActions,
            clock,
            new CompanyGracePolicy(7));

        var priceCatalog = new global::HabloTruckPlatform.Application.Integrations.Stripex.StripeOptions
        {
            WebhookSecret = "whsec_test",
            StripeSecretKey = "sk_test",
            IndividualMonthlyPriceId = "price_ind_monthly",
            IndividualYearlyPriceId = "price_ind_yearly",
            FleetSeatMonthlyPriceId = "price_fleet_monthly"
        };

        var handler = new StripeSubscriptionHandler(
            userResolver,
            userStore,
            graceIndexStore,
            manyChat,
            orchestrator,
            clock,
            new GracePolicy(72),
            priceCatalog,
            companyStore,
            entitlementStore,
            expiryIndexStore,
            NullLogger<StripeSubscriptionHandler>.Instance);

        return new HandlerFixture(handler, userStore, userResolver, manyChat, priceCatalog);
    }

    private sealed record HandlerFixture(
        StripeSubscriptionHandler Handler,
        InMemoryUserStore UserStore,
        InMemoryUserResolver UserResolver,
        RecordingManyChatSync ManyChatSync,
        global::HabloTruckPlatform.Application.Integrations.Stripex.StripeOptions PriceCatalog);

    private static DateTimeOffset Utc(int y, int m, int d, int h)
        => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserResolver : IUserResolver
    {
        private readonly Dictionary<string, UserRef> _byStripe = new(StringComparer.OrdinalIgnoreCase);

        public void Map(string stripeCustomerId, User user)
            => _byStripe[stripeCustomerId] = new UserRef(Buckets.UserBucketPk(user.UserId), user.UserId);

        public Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult(_byStripe.TryGetValue(stripeCustomerId, out var userRef) ? (UserRef?)userRef : null);

        public Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);

        public Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _users = new();

        public void Add(User user)
            => _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public User? GetById(string userId)
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

    private sealed class InMemoryGraceIndexStore : IGraceIndexStore
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

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public int SyncCalls { get; private set; }
        public int PaymentFailedFlowCalls { get; private set; }

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
        {
            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
        {
            PaymentFailedFlowCalls++;
            return Task.CompletedTask;
        }

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
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

    private sealed class InMemoryEntitlementExpiryIndexStore : IEntitlementExpiryIndexStore
    {
        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(EntitlementRef entitlementRef, DateTimeOffset endUtc, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EntitlementExpiryIndexItem>> QueryExpiringAsync(string expiryPk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitlementExpiryIndexItem>>(Array.Empty<EntitlementExpiryIndexItem>());

        public Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
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
