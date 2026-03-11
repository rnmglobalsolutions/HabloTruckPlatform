using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeSubscriptionHandlerCancelFlowTests
{
    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_IgnoreEvent_When_EventIsOutOfOrder()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_OOO_1",
            StripeCustomerId = "cus_ooo_1",
            StripeSubscriptionId = "sub_ooo_1",
            SubscriptionStatus = "active",
            LastStripeEventCreatedUtc = now,
            EffectiveAccess = new AccessSnapshot(AccessMode.Full, AccessSource.Individual, null)
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_ooo_1", user);

        // Act
        var result = await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_old_1",
            StripeEventCreatedUtc: now.AddMinutes(-5),
            StripeCustomerId: "cus_ooo_1",
            StripeSubscriptionId: "sub_ooo_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(20),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        // Assert
        Assert.NotNull(result);
        Assert.Contains("out-of-order", result!.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.UserStore.UpsertCalls);
        Assert.Equal(0, fixture.EntitlementStore.UpsertCalls);
    }

    [Fact]
    public async Task HandleSubscriptionDeletedAsync_Should_IgnoreEvent_When_EventIsOutOfOrder()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_OOO_2",
            StripeCustomerId = "cus_ooo_2",
            StripeSubscriptionId = "sub_ooo_2",
            SubscriptionStatus = "active",
            LastStripeEventCreatedUtc = now,
            EffectiveAccess = new AccessSnapshot(AccessMode.Full, AccessSource.Individual, null)
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_ooo_2", user);

        // Act
        var result = await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_old_2",
            StripeEventCreatedUtc: now.AddMinutes(-3),
            StripeCustomerId: "cus_ooo_2",
            StripeSubscriptionId: "sub_ooo_2",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now,
            CanceledAtUtc: now,
            EndedAtUtc: now));

        // Assert
        Assert.NotNull(result);
        Assert.Contains("out-of-order", result!.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.UserStore.UpsertCalls);
        Assert.Equal(0, fixture.EntitlementStore.UpsertCalls);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_ProjectFleetEntitlementEnd_When_CancelAtPeriodEndIsScheduled()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var periodEnd = now.AddDays(27);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_FLEET_1",
            StripeCustomerId = "cus_fleet_1",
            StripeSubscriptionId = "sub_fleet_1",
            SubscriptionStatus = "active"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_1", user);

        fixture.CompanyStore.Companies["C_FLEET_1"] = new Company
        {
            CompanyId = "C_FLEET_1",
            StripeCustomerId = "cus_fleet_1",
            AdminEmailNormalized = "admin@fleet1.com",
            Status = "active"
        };

        fixture.EntitlementStore.UpsertLocal(new Entitlement
        {
            CompanyId = "C_FLEET_1",
            EntitlementId = "ent_sub_fleet_1",
            SeatsTotal = 25,
            SeatsUsed = 4,
            Status = "active",
            StartUtc = now.AddDays(-10),
            EndUtc = null,
            UpdatedAtUtc = now.AddDays(-1)
        });

        // Act
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_update_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_1",
            StripeSubscriptionId: "sub_fleet_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: null,
            EndedAtUtc: null));

        // Assert
        var ent = await fixture.EntitlementStore.GetAsync("C_FLEET_1", "ent_sub_fleet_1");
        Assert.NotNull(ent);
        Assert.Equal("active", ent!.Status);
        Assert.Equal(periodEnd, ent.EndUtc);
        Assert.Single(fixture.ExpiryIndexStore.Upserts);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_MoveExpiryIndex_When_PeriodEndChanges()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var oldPeriodEnd = now.AddDays(10);
        var newPeriodEnd = now.AddDays(25);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_FLEET_1B",
            StripeCustomerId = "cus_fleet_1b",
            StripeSubscriptionId = "sub_fleet_1b",
            SubscriptionStatus = "active"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_1b", user);

        fixture.CompanyStore.Companies["C_FLEET_1B"] = new Company
        {
            CompanyId = "C_FLEET_1B",
            StripeCustomerId = "cus_fleet_1b",
            AdminEmailNormalized = "admin@fleet1b.com",
            Status = "active"
        };

        fixture.EntitlementStore.UpsertLocal(new Entitlement
        {
            CompanyId = "C_FLEET_1B",
            EntitlementId = "ent_sub_fleet_1b",
            SeatsTotal = 25,
            SeatsUsed = 5,
            Status = "active",
            StartUtc = now.AddDays(-15),
            EndUtc = oldPeriodEnd,
            UpdatedAtUtc = now.AddDays(-1)
        });

        // Act
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_update_1b",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_1b",
            StripeSubscriptionId: "sub_fleet_1b",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: newPeriodEnd,
            CanceledAtUtc: null,
            EndedAtUtc: null));

        // Assert
        var ent = await fixture.EntitlementStore.GetAsync("C_FLEET_1B", "ent_sub_fleet_1b");
        Assert.NotNull(ent);
        Assert.Equal("active", ent!.Status);
        Assert.Equal(newPeriodEnd, ent.EndUtc);

        Assert.Single(fixture.ExpiryIndexStore.Upserts);
        Assert.Equal(newPeriodEnd, fixture.ExpiryIndexStore.Upserts[0].EndUtc);

        Assert.Single(fixture.ExpiryIndexStore.Deletes);
        var expectedDeletePk = $"{TablePrefixes.EntitlementExpiry}_{oldPeriodEnd:yyyyMMdd}";
        var expectedDeleteRk = $"{oldPeriodEnd.Ticks:D19}_C_FLEET_1B_ent_sub_fleet_1b";
        Assert.Equal((expectedDeletePk, expectedDeleteRk), fixture.ExpiryIndexStore.Deletes[0]);
    }

    [Fact]
    public async Task HandleSubscriptionDeletedAsync_Should_ExpireFleetEntitlement_When_SubscriptionIsDeleted()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_FLEET_2",
            StripeCustomerId = "cus_fleet_2",
            StripeSubscriptionId = "sub_fleet_2",
            SubscriptionStatus = "active"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_2", user);

        fixture.CompanyStore.Companies["C_FLEET_2"] = new Company
        {
            CompanyId = "C_FLEET_2",
            StripeCustomerId = "cus_fleet_2",
            AdminEmailNormalized = "admin@fleet2.com",
            Status = "active"
        };

        fixture.EntitlementStore.UpsertLocal(new Entitlement
        {
            CompanyId = "C_FLEET_2",
            EntitlementId = "ent_sub_fleet_2",
            SeatsTotal = 30,
            SeatsUsed = 8,
            Status = "active",
            StartUtc = now.AddDays(-30),
            EndUtc = now.AddDays(5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        // Act
        await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_fleet_deleted_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_2",
            StripeSubscriptionId: "sub_fleet_2",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: now,
            CanceledAtUtc: now,
            EndedAtUtc: now));

        // Assert
        var ent = await fixture.EntitlementStore.GetAsync("C_FLEET_2", "ent_sub_fleet_2");
        Assert.NotNull(ent);
        Assert.Equal("expired", ent!.Status);
        Assert.Equal(now, ent.EndUtc);
    }

    [Fact]
    public async Task HandleSubscriptionDeletedAsync_Should_KeepFleetEntitlementActive_Until_FutureEndedAt()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var futureEnd = now.AddDays(4);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_FLEET_3",
            StripeCustomerId = "cus_fleet_3",
            StripeSubscriptionId = "sub_fleet_3",
            SubscriptionStatus = "active"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_3", user);

        fixture.CompanyStore.Companies["C_FLEET_3"] = new Company
        {
            CompanyId = "C_FLEET_3",
            StripeCustomerId = "cus_fleet_3",
            AdminEmailNormalized = "admin@fleet3.com",
            Status = "active"
        };

        fixture.EntitlementStore.UpsertLocal(new Entitlement
        {
            CompanyId = "C_FLEET_3",
            EntitlementId = "ent_sub_fleet_3",
            SeatsTotal = 10,
            SeatsUsed = 2,
            Status = "active",
            StartUtc = now.AddDays(-20),
            EndUtc = null,
            UpdatedAtUtc = now.AddDays(-1)
        });

        // Act
        await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_fleet_deleted_3",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_3",
            StripeSubscriptionId: "sub_fleet_3",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: futureEnd,
            CanceledAtUtc: now,
            EndedAtUtc: futureEnd));

        // Assert
        var ent = await fixture.EntitlementStore.GetAsync("C_FLEET_3", "ent_sub_fleet_3");
        Assert.NotNull(ent);
        Assert.Equal("active", ent!.Status);
        Assert.Equal(futureEnd, ent.EndUtc);
        Assert.Single(fixture.ExpiryIndexStore.Upserts);
        Assert.Equal(futureEnd, fixture.ExpiryIndexStore.Upserts[0].EndUtc);
    }


    [Fact]
    public async Task HandleFleetLifecycleSequence_Should_ProjectCancelSchedule_IgnoreOlderReplay_AndExpireOnFinalDelete()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var periodEnd = now.AddDays(5);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_FLEET_SEQ_1",
            StripeCustomerId = "cus_fleet_seq_1",
            StripeSubscriptionId = "sub_fleet_seq_1",
            SubscriptionStatus = "active"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_seq_1", user);

        fixture.CompanyStore.Companies["C_FLEET_SEQ_1"] = new Company
        {
            CompanyId = "C_FLEET_SEQ_1",
            StripeCustomerId = "cus_fleet_seq_1",
            AdminEmailNormalized = "admin@fleet-seq.com",
            Status = "active"
        };

        fixture.EntitlementStore.UpsertLocal(new Entitlement
        {
            CompanyId = "C_FLEET_SEQ_1",
            EntitlementId = "ent_sub_fleet_seq_1",
            SeatsTotal = 20,
            SeatsUsed = 6,
            Status = "active",
            StartUtc = now.AddDays(-10),
            EndUtc = null,
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_seq_update_1",
            StripeEventCreatedUtc: now.AddMinutes(1),
            StripeCustomerId: "cus_fleet_seq_1",
            StripeSubscriptionId: "sub_fleet_seq_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: now.AddMinutes(1),
            EndedAtUtc: null));

        var scheduledEntitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_SEQ_1", "ent_sub_fleet_seq_1");
        Assert.NotNull(scheduledEntitlement);
        Assert.Equal("active", scheduledEntitlement!.Status);
        Assert.Equal(periodEnd, scheduledEntitlement.EndUtc);

        await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_fleet_seq_deleted_future",
            StripeEventCreatedUtc: now.AddMinutes(2),
            StripeCustomerId: "cus_fleet_seq_1",
            StripeSubscriptionId: "sub_fleet_seq_1",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: now.AddMinutes(2),
            EndedAtUtc: periodEnd));

        var futureEndedEntitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_SEQ_1", "ent_sub_fleet_seq_1");
        Assert.NotNull(futureEndedEntitlement);
        Assert.Equal("active", futureEndedEntitlement!.Status);
        Assert.Equal(periodEnd, futureEndedEntitlement.EndUtc);

        var outOfOrder = await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_seq_old_replay",
            StripeEventCreatedUtc: now.AddMinutes(1),
            StripeCustomerId: "cus_fleet_seq_1",
            StripeSubscriptionId: "sub_fleet_seq_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(2),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        Assert.NotNull(outOfOrder);
        Assert.Contains("out-of-order", outOfOrder!.Reason, StringComparison.OrdinalIgnoreCase);

        await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_fleet_seq_deleted_final",
            StripeEventCreatedUtc: now.AddMinutes(3),
            StripeCustomerId: "cus_fleet_seq_1",
            StripeSubscriptionId: "sub_fleet_seq_1",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: now,
            CanceledAtUtc: now,
            EndedAtUtc: now));

        var finalEntitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_SEQ_1", "ent_sub_fleet_seq_1");
        Assert.NotNull(finalEntitlement);
        Assert.Equal("expired", finalEntitlement!.Status);
        Assert.Equal(now, finalEntitlement.EndUtc);
    }
    private static HandlerFixture BuildFixture(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        var userStore = new InMemoryUserStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var userResolver = new InMemoryUserResolver();
        var graceIndexStore = new InMemoryGraceIndexStore();
        var manyChat = new NoopManyChatSync();
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

        return new HandlerFixture(handler, userStore, userResolver, companyStore, entitlementStore, expiryIndexStore, priceCatalog);
    }

    private sealed record HandlerFixture(
        StripeSubscriptionHandler Handler,
        InMemoryUserStore UserStore,
        InMemoryUserResolver UserResolver,
        InMemoryCompanyStore CompanyStore,
        InMemoryEntitlementStore EntitlementStore,
        InMemoryEntitlementExpiryIndexStore ExpiryIndexStore,
        global::HabloTruckPlatform.Application.Integrations.Stripex.StripeOptions PriceCatalog);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserResolver : IUserResolver
    {
        private readonly Dictionary<string, UserRef> _byStripe = new(StringComparer.OrdinalIgnoreCase);

        public void Map(string stripeCustomerId, User user)
        {
            _byStripe[stripeCustomerId] = new UserRef(Buckets.UserBucketPk(user.UserId), user.UserId);
        }

        public Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
        {
            if (_byStripe.TryGetValue(stripeCustomerId, out var userRef))
                return Task.FromResult<UserRef?>(userRef);

            return Task.FromResult<UserRef?>(null);
        }

        public Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);

        public Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _users = new();
        public int UpsertCalls { get; private set; }

        public void Add(User user)
        {
            _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
        }

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
        {
            _users.TryGetValue((userPk, userId), out var user);
            return Task.FromResult(user);
        }

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            UpsertCalls++;
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

    private sealed class InMemoryCompanyStore : ICompanyStore
    {
        public Dictionary<string, Company> Companies { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
        {
            Companies.TryGetValue(companyId, out var company);
            return Task.FromResult(company);
        }

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
        {
            var company = Companies.Values.FirstOrDefault(c => string.Equals(c.StripeCustomerId, stripeCustomerId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(company);
        }

        public Task UpsertAsync(Company company, CancellationToken ct = default)
        {
            Companies[company.CompanyId] = company;
            return Task.CompletedTask;
        }

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _items = new();
        public int UpsertCalls { get; private set; }

        public void UpsertLocal(Entitlement entitlement)
        {
            _items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
        }

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
        {
            _items.TryGetValue((companyId, entitlementId), out var entitlement);
            return Task.FromResult(entitlement);
        }

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            UpsertCalls++;
            _items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public async Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
        {
            var ent = await GetAsync(companyId, entitlementId, ct);
            if (ent is null)
                return;

            ent.Status = status;
            _items[(companyId, entitlementId)] = ent;
        }
    }

    private sealed class InMemoryEntitlementExpiryIndexStore : IEntitlementExpiryIndexStore
    {
        public List<(EntitlementRef Entitlement, DateTimeOffset EndUtc)> Upserts { get; } = new();
        public List<(string Pk, string Rk)> Deletes { get; } = new();

        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(EntitlementRef entitlementRef, DateTimeOffset endUtc, CancellationToken ct = default)
        {
            Upserts.Add((entitlementRef, endUtc));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EntitlementExpiryIndexItem>> QueryExpiringAsync(string expiryPk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitlementExpiryIndexItem>>(Array.Empty<EntitlementExpiryIndexItem>());

        public Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
        {
            Deletes.Add((pk, rk));
            return Task.CompletedTask;
        }
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



