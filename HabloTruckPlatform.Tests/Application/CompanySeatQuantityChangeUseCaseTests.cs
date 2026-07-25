using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class CompanySeatQuantityChangeUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Should_IncreaseSeats_Immediately()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 15
        });

        Assert.True(result.Result);
        Assert.Equal("increase", result.Direction);
        Assert.Equal("immediate", result.EffectiveWhen);
        Assert.Equal(15, fixture.Gateway.LastTargetQuantity);
        Assert.Equal("always_invoice", fixture.Gateway.LastProrationBehavior);
        Assert.Equal(15, fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].SeatsTotal);
        Assert.False(fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].IsOverCapacity);
    }

    [Fact]
    public async Task ExecuteAsync_Should_DecreaseSeatsSafely_WithoutProration()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 8
        });

        Assert.True(result.Result);
        Assert.Equal("decrease", result.Direction);
        Assert.Equal("next_invoice", result.EffectiveWhen);
        Assert.Equal(8, fixture.Gateway.LastTargetQuantity);
        Assert.Equal("none", fixture.Gateway.LastProrationBehavior);
        Assert.Equal(8, fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].SeatsTotal);
        Assert.False(fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].IsOverCapacity);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectDecreaseBelowSeatsUsed()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 5
        });

        Assert.False(result.Result);
        Assert.Equal("target_below_seats_used", result.Error);
        Assert.Equal("next_invoice", result.EffectiveWhen);
        Assert.Null(fixture.Gateway.LastTargetQuantity);
        Assert.Equal(10, fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].SeatsTotal);
        Assert.Empty(fixture.AdminPaymentAlerts.Alerts);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotifyAdmin_WhenSeatIncreaseStripeUpdateThrows()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);
        fixture.Gateway.UpdateException = new InvalidOperationException("Stripe rejected invoice payment");

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 15
        });

        Assert.False(result.Result);
        Assert.Equal("stripe_update_failed", result.Error);
        Assert.Equal(10, fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].SeatsTotal);

        var alert = Assert.Single(fixture.AdminPaymentAlerts.Alerts);
        Assert.Equal("company_seat_quantity_change", alert.OperationName);
        Assert.Equal("company_seat_quantity_increase", alert.FailureStage);
        Assert.Equal("stripe_update_failed", alert.FailureReason);
        Assert.Equal("Critical", alert.Severity);
        Assert.Equal("ADMIN", alert.UserId);
        Assert.Equal("admin@company.com", alert.Email);
        Assert.Equal("+15551234567", alert.PhoneE164);
        Assert.Equal("mc_admin", alert.ManyChatSubscriberId);
        Assert.Equal("C1", alert.CompanyId);
        Assert.Equal("cus_company", alert.StripeCustomerId);
        Assert.Equal("sub_fleet_1", alert.StripeSubscriptionId);
        Assert.Equal("fleet", alert.PlanType);
        Assert.Equal("price_fleet", alert.PriceId);
        Assert.Equal("10", alert.Details["previousSeats"]);
        Assert.Equal("15", alert.Details["targetSeats"]);
        Assert.Equal("6", alert.Details["seatsUsed"]);
        Assert.Equal("immediate", alert.Details["effectiveWhen"]);
        Assert.Equal("always_invoice", alert.Details["prorationBehavior"]);
        Assert.Equal("RNM Fleet", alert.Details["companyName"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_NotifyAdmin_WhenSeatIncreaseStripeUpdateReturnsNull()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);
        fixture.Gateway.Updated = null;

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 15
        });

        Assert.False(result.Result);
        Assert.Equal("stripe_update_failed", result.Error);

        var alert = Assert.Single(fixture.AdminPaymentAlerts.Alerts);
        Assert.Equal("company_seat_quantity_change", alert.OperationName);
        Assert.Equal("company_seat_quantity_increase", alert.FailureStage);
        Assert.Equal("stripe_update_returned_null", alert.FailureReason);
        Assert.Equal("15", alert.Details["targetSeats"]);
    }

    [Fact]
    public async Task ExecuteAsync_Should_PreserveStripeFailure_WhenAdminAlertThrows()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);
        fixture.Gateway.UpdateException = new InvalidOperationException("Stripe rejected invoice payment");
        fixture.AdminPaymentAlerts.ExceptionToThrow = new InvalidOperationException("SendGrid client failed unexpectedly");

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 15
        });

        Assert.False(result.Result);
        Assert.Equal("stripe_update_failed", result.Error);
        Assert.Equal(10, fixture.Entitlements.Items[("C1", "ent_sub_fleet_1")].SeatsTotal);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectDeferredSeatIncrease()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 15,
            EffectiveWhen = "next_invoice"
        });

        Assert.False(result.Result);
        Assert.Equal("invalid_effective_when_for_increase", result.Error);
        Assert.Null(fixture.Gateway.LastTargetQuantity);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectImmediateSeatDecrease()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 8,
            EffectiveWhen = "immediate"
        });

        Assert.False(result.Result);
        Assert.Equal("invalid_effective_when_for_decrease", result.Error);
        Assert.Null(fixture.Gateway.LastTargetQuantity);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectInvalidEffectiveWhen_EvenWhenQuantityIsUnchanged()
    {
        var now = Utc(2026, 4, 14);
        var fixture = BuildFixture(now, seatsTotal: 10, seatsUsed: 6);

        var result = await fixture.Sut.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "ADMIN",
            CompanyId = "C1",
            SubscriptionId = "sub_fleet_1",
            TargetSeats = 10,
            EffectiveWhen = "someday"
        });

        Assert.False(result.Result);
        Assert.Equal("invalid_effective_when", result.Error);
        Assert.Null(fixture.Gateway.LastTargetQuantity);
    }

    private static Fixture BuildFixture(DateTimeOffset now, int seatsTotal, int seatsUsed)
    {
        var users = new InMemoryUserStore();
        users.Users[("U_PK", "ADMIN")] = new User
        {
            UserId = "ADMIN",
            EmailNormalized = "admin@company.com",
            PhoneE164 = "+15551234567",
            ManyChatSubscriberId = "mc_admin",
            CompanyId = "C1",
            PlanType = "individual_monthly"
        };

        var companies = new InMemoryCompanyStore();
        companies.Items["C1"] = new Company
        {
            CompanyId = "C1",
            Name = "RNM Fleet",
            AdminEmailNormalized = "admin@company.com",
            StripeCustomerId = "cus_company"
        };

        var entitlements = new InMemoryEntitlementStore();
        entitlements.Items[("C1", "ent_sub_fleet_1")] = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "ent_sub_fleet_1",
            SeatsTotal = seatsTotal,
            SeatsUsed = seatsUsed,
            IsOverCapacity = seatsUsed > seatsTotal,
            StartUtc = now.AddMonths(-1),
            Status = "active",
            UpdatedAtUtc = now
        };

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot("sub_fleet_1", "cus_company", "active", "price_fleet", "month", seatsTotal, false, now.AddDays(20), null, null),
            Updated = new StripeSubscriptionSnapshot("sub_fleet_1", "cus_company", "active", "price_fleet", "month", seatsTotal, false, now.AddDays(20), null, null)
        };

        var adminPaymentAlerts = new RecordingAdminPaymentAlertNotifier();
        var sut = new UpdateCompanySeatQuantityUseCase(
            users,
            companies,
            entitlements,
            gateway,
            new FixedClock(now),
            NullLogger<UpdateCompanySeatQuantityUseCase>.Instance,
            adminPaymentAlerts: adminPaymentAlerts);

        return new Fixture(sut, entitlements, gateway, adminPaymentAlerts);
    }

    private static DateTimeOffset Utc(int year, int month, int day)
        => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(
        UpdateCompanySeatQuantityUseCase Sut,
        InMemoryEntitlementStore Entitlements,
        FakeStripeSubscriptionGateway Gateway,
        RecordingAdminPaymentAlertNotifier AdminPaymentAlerts);

    private sealed class RecordingAdminPaymentAlertNotifier : IAdminPaymentAlertNotifier
    {
        public List<AdminPaymentAlert> Alerts { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task<AdminPaymentAlertDeliveryResult> NotifyAsync(AdminPaymentAlert alert, CancellationToken ct = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            Alerts.Add(alert);
            return Task.FromResult(AdminPaymentAlertDeliveryResult.Sent());
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        public Dictionary<(string Pk, string Id), User> Users { get; } = [];

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(Users.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpsertLookupsAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<User>>(Users.Values.ToArray());
        public Task<StripeUserScanPage> QueryUsersWithStripePageAsync(int take = 500, int startBucket = 0, CancellationToken ct = default) => Task.FromResult(new StripeUserScanPage([], 0, 0, 0, true));
    }

    private sealed class InMemoryCompanyStore : ICompanyStore
    {
        public Dictionary<string, Company> Items { get; } = [];

        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
            => Task.FromResult(Items.TryGetValue(companyId, out var company) ? company : null);

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult(Items.Values.FirstOrDefault(c => c.StripeCustomerId == stripeCustomerId));

        public Task UpsertAsync(Company company, CancellationToken ct = default)
        {
            Items[company.CompanyId] = company;
            return Task.CompletedTask;
        }

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        public Dictionary<(string CompanyId, string EntitlementId), Entitlement> Items { get; } = [];

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(Items.TryGetValue((companyId, entitlementId), out var entitlement) ? entitlement : null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            Items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            Items[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeStripeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public StripeSubscriptionSnapshot? Current { get; set; }
        public StripeSubscriptionSnapshot? Updated { get; set; }
        public Exception? UpdateException { get; set; }
        public int? LastTargetQuantity { get; private set; }
        public string? LastProrationBehavior { get; private set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(Current);

        public Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(string subscriptionId, string idempotencyKey, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<StripeSubscriptionSnapshot?> UpdateSubscriptionQuantityAsync(string subscriptionId, int targetQuantity, string prorationBehavior, string idempotencyKey, CancellationToken ct = default)
        {
            LastTargetQuantity = targetQuantity;
            LastProrationBehavior = prorationBehavior;

            if (UpdateException is not null)
                throw UpdateException;

            Updated = Updated is null
                ? null
                : Updated with { Quantity = targetQuantity };
            return Task.FromResult(Updated);
        }
    }
}
