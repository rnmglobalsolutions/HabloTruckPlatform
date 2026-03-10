using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class CancelSubscriptionAtPeriodEndUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Should_ReturnAlreadyScheduled_When_SubscriptionAlreadyMarkedForPeriodEndCancel()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var companyStore = new InMemoryCompanyStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_ind_1",
                CustomerId: "cus_ind_1",
                Status: "active",
                PriceId: "price_ind_monthly",
                Interval: "month",
                CancelAtPeriodEnd: true,
                CurrentPeriodEndUtc: now.AddDays(18),
                CanceledAtUtc: now.AddMinutes(-2),
                EndedAtUtc: null)
        };

        userStore.Users[("HT_U_001", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_ind_1",
            StripeSubscriptionId = "sub_ind_1",
            EmailNormalized = "owner@hablotruck.com"
        };

        var sut = new CancelSubscriptionAtPeriodEndUseCase(
            userStore,
            companyStore,
            entitlementStore,
            gateway,
            new FixedClock(now));

        // Act
        var result = await sut.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = "individual",
            ActorUserPk = "HT_U_001",
            ActorUserId = "U1"
        });

        // Assert
        Assert.True(result.Result);
        Assert.True(result.AlreadyScheduled);
        Assert.Equal("sub_ind_1", result.SubscriptionId);
        Assert.Equal(0, gateway.ScheduleCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ScheduleCancel_When_IndividualActorIsAuthorized()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var companyStore = new InMemoryCompanyStore();
        var entitlementStore = new InMemoryEntitlementStore();

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_ind_2",
                CustomerId: "cus_ind_2",
                Status: "active",
                PriceId: "price_ind_year",
                Interval: "year",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(40),
                CanceledAtUtc: null,
                EndedAtUtc: null),
            Updated = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_ind_2",
                CustomerId: "cus_ind_2",
                Status: "active",
                PriceId: "price_ind_year",
                Interval: "year",
                CancelAtPeriodEnd: true,
                CurrentPeriodEndUtc: now.AddDays(40),
                CanceledAtUtc: now,
                EndedAtUtc: null)
        };

        userStore.Users[("HT_U_002", "U2")] = new User
        {
            UserId = "U2",
            StripeCustomerId = "cus_ind_2",
            StripeSubscriptionId = "sub_ind_2",
            EmailNormalized = "driver@hablotruck.com"
        };

        var sut = new CancelSubscriptionAtPeriodEndUseCase(
            userStore,
            companyStore,
            entitlementStore,
            gateway,
            new FixedClock(now));

        // Act
        var result = await sut.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = "individual",
            ActorUserPk = "HT_U_002",
            ActorUserId = "U2"
        });

        // Assert
        Assert.True(result.Result);
        Assert.False(result.AlreadyScheduled);
        Assert.True(result.CancelAtPeriodEnd);
        Assert.Equal("sub_ind_2", result.SubscriptionId);
        Assert.Equal(1, gateway.ScheduleCalls);
        Assert.Equal(1, userStore.UpsertCalls);
        Assert.True(userStore.Users[("HT_U_002", "U2")].StripeCancelAtPeriodEnd);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnForbidden_When_CompanyActorIsNotAuthorized()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var companyStore = new InMemoryCompanyStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var gateway = new FakeStripeSubscriptionGateway();

        userStore.Users[("HT_U_003", "U3")] = new User
        {
            UserId = "U3",
            EmailNormalized = "other@hablotruck.com"
        };

        companyStore.Companies["C1"] = new Company
        {
            CompanyId = "C1",
            AdminEmailNormalized = "admin@fleet.com",
            StripeCustomerId = "cus_company_1"
        };

        var sut = new CancelSubscriptionAtPeriodEndUseCase(
            userStore,
            companyStore,
            entitlementStore,
            gateway,
            new FixedClock(now));

        // Act
        var result = await sut.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = "company",
            ActorUserPk = "HT_U_003",
            ActorUserId = "U3",
            CompanyId = "C1",
            SubscriptionId = "sub_company_1"
        });

        // Assert
        Assert.False(result.Result);
        Assert.Equal("forbidden", result.Error);
        Assert.Equal(0, gateway.ScheduleCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ScheduleCancel_When_CompanyActorIsAuthorized()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var companyStore = new InMemoryCompanyStore();
        var entitlementStore = new InMemoryEntitlementStore();

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_company_2",
                CustomerId: "cus_company_2",
                Status: "active",
                PriceId: "price_fleet_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(30),
                CanceledAtUtc: null,
                EndedAtUtc: null),
            Updated = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_company_2",
                CustomerId: "cus_company_2",
                Status: "active",
                PriceId: "price_fleet_monthly",
                Interval: "month",
                CancelAtPeriodEnd: true,
                CurrentPeriodEndUtc: now.AddDays(30),
                CanceledAtUtc: now,
                EndedAtUtc: null)
        };

        userStore.Users[("HT_U_004", "U4")] = new User
        {
            UserId = "U4",
            CompanyId = "C2",
            EmailNormalized = "admin@fleet2.com",
            StripeCustomerId = "cus_company_2"
        };

        companyStore.Companies["C2"] = new Company
        {
            CompanyId = "C2",
            AdminEmailNormalized = "admin@fleet2.com",
            StripeCustomerId = "cus_company_2"
        };

        entitlementStore.UpsertLocal(new Entitlement
        {
            CompanyId = "C2",
            EntitlementId = "ent_sub_company_2",
            SeatsTotal = 20,
            SeatsUsed = 3,
            Status = "active",
            StartUtc = now.AddDays(-20),
            EndUtc = null,
            UpdatedAtUtc = now.AddDays(-1)
        });

        var sut = new CancelSubscriptionAtPeriodEndUseCase(
            userStore,
            companyStore,
            entitlementStore,
            gateway,
            new FixedClock(now));

        // Act
        var result = await sut.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = "company",
            ActorUserPk = "HT_U_004",
            ActorUserId = "U4",
            CompanyId = "C2",
            SubscriptionId = "sub_company_2"
        });

        // Assert
        Assert.True(result.Result);
        Assert.Equal("company", result.Scope);
        Assert.Equal("sub_company_2", result.SubscriptionId);
        Assert.True(result.CancelAtPeriodEnd);
        Assert.Equal(1, gateway.ScheduleCalls);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ReturnCompanyEntitlementNotFound_When_CompanySubscriptionDoesNotMatchEntitlementPath()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var companyStore = new InMemoryCompanyStore();
        var entitlementStore = new InMemoryEntitlementStore();

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_company_3",
                CustomerId: "cus_company_3",
                Status: "active",
                PriceId: "price_fleet_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(30),
                CanceledAtUtc: null,
                EndedAtUtc: null)
        };

        userStore.Users[("HT_U_005", "U5")] = new User
        {
            UserId = "U5",
            CompanyId = "C3",
            EmailNormalized = "admin@fleet3.com",
            StripeCustomerId = "cus_company_3"
        };

        companyStore.Companies["C3"] = new Company
        {
            CompanyId = "C3",
            AdminEmailNormalized = "admin@fleet3.com",
            StripeCustomerId = "cus_company_3"
        };

        var sut = new CancelSubscriptionAtPeriodEndUseCase(
            userStore,
            companyStore,
            entitlementStore,
            gateway,
            new FixedClock(now));

        // Act
        var result = await sut.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = "company",
            ActorUserPk = "HT_U_005",
            ActorUserId = "U5",
            CompanyId = "C3",
            SubscriptionId = "sub_company_3"
        });

        // Assert
        Assert.False(result.Result);
        Assert.Equal("company_entitlement_not_found", result.Error);
        Assert.Equal(0, gateway.ScheduleCalls);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        public Dictionary<(string Pk, string Id), User> Users { get; } = new();
        public int UpsertCalls { get; private set; }

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
        {
            Users.TryGetValue((userPk, userId), out var user);
            return Task.FromResult(user);
        }

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            UpsertCalls++;
            Users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
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
        {
            Companies[companyId] = new Company
            {
                CompanyId = companyId,
                Name = companyName,
                AdminEmailNormalized = adminEmailNormalized,
                StripeCustomerId = stripeCustomerId,
                Status = "active"
            };
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _items = new();

        public void UpsertLocal(Entitlement entitlement)
            => _items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;

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
            _items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public async Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
        {
            var entitlement = await GetAsync(companyId, entitlementId, ct);
            if (entitlement is null)
                return;

            entitlement.Status = status;
            _items[(companyId, entitlementId)] = entitlement;
        }
    }

    private sealed class FakeStripeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public StripeSubscriptionSnapshot? Current { get; set; }
        public StripeSubscriptionSnapshot? Updated { get; set; }
        public int ScheduleCalls { get; private set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(Current);

        public Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(string subscriptionId, string idempotencyKey, CancellationToken ct = default)
        {
            ScheduleCalls++;
            return Task.FromResult(Updated);
        }
    }
}

