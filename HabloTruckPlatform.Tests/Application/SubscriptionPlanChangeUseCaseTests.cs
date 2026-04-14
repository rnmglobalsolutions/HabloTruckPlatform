using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class SubscriptionPlanChangeUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Should_UpgradeMonthlyToYearly_Immediately()
    {
        var now = Utc(2026, 4, 14);
        var users = new InMemoryUserStore();
        users.Users[("U_PK", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_1",
            StripeSubscriptionId = "sub_1",
            StripePriceId = "price_monthly",
            PlanType = "individual_monthly",
            IndividualPlanTerm = "monthly",
            SubscriptionStatus = "active"
        };

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_monthly", "month", 1, false, now.AddDays(20), null, null),
            Changed = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_yearly", "year", 1, false, now.AddYears(1), null, null)
        };
        var metrics = new RecordingMetrics();
        var sut = BuildSut(users, gateway, now, metrics);

        var result = await sut.ExecuteAsync(new ChangeSubscriptionPlanRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "U1",
            TargetPlanType = "individual_yearly"
        });

        Assert.True(result.Result);
        Assert.Equal("individual_yearly", result.TargetPlanType);
        Assert.Equal("immediate", result.EffectiveWhen);
        Assert.Equal("price_yearly", gateway.LastTargetPriceId);
        Assert.Equal("always_invoice", gateway.LastProrationBehavior);
        Assert.Equal("now", gateway.LastBillingCycleAnchor);
        Assert.Equal("individual_yearly", users.Users[("U_PK", "U1")].PlanType);
        Assert.Equal("annual", users.Users[("U_PK", "U1")].IndividualPlanTerm);
        Assert.Contains(metrics.SubscriptionPlanChanges, m => m.Outcome == "completed" && m.TargetPlanType == "individual_yearly");
    }

    [Fact]
    public async Task ExecuteAsync_Should_DowngradeYearlyToMonthly_NextInvoiceWithoutProration()
    {
        var now = Utc(2026, 4, 14);
        var users = new InMemoryUserStore();
        users.Users[("U_PK", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_1",
            StripeSubscriptionId = "sub_1",
            StripePriceId = "price_yearly",
            PlanType = "individual_yearly",
            IndividualPlanTerm = "annual",
            SubscriptionStatus = "active"
        };

        var periodEnd = now.AddMonths(9);
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_yearly", "year", 1, false, periodEnd, null, null),
            Changed = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_monthly", "month", 1, false, periodEnd, null, null)
        };
        var sut = BuildSut(users, gateway, now);

        var result = await sut.ExecuteAsync(new ChangeSubscriptionPlanRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "U1",
            TargetPlanType = "individual_monthly"
        });

        Assert.True(result.Result);
        Assert.Equal("next_invoice", result.EffectiveWhen);
        Assert.Equal("price_monthly", gateway.LastTargetPriceId);
        Assert.Equal("none", gateway.LastProrationBehavior);
        Assert.Null(gateway.LastBillingCycleAnchor);
        Assert.Equal("individual_monthly", users.Users[("U_PK", "U1")].PlanType);
        Assert.Equal("monthly", users.Users[("U_PK", "U1")].IndividualPlanTerm);
        Assert.Equal(periodEnd, users.Users[("U_PK", "U1")].StripeCurrentPeriodEndUtc);
    }

    [Fact]
    public async Task ExecuteAsync_Should_TreatPeriodEndAsLegacyAliasForNextInvoice()
    {
        var now = Utc(2026, 4, 14);
        var users = new InMemoryUserStore();
        users.Users[("U_PK", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_1",
            StripeSubscriptionId = "sub_1",
            StripePriceId = "price_yearly",
            PlanType = "individual_yearly",
            IndividualPlanTerm = "annual",
            SubscriptionStatus = "active"
        };

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_yearly", "year", 1, false, now.AddMonths(9), null, null),
            Changed = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_monthly", "month", 1, false, now.AddMonths(9), null, null)
        };
        var sut = BuildSut(users, gateway, now);

        var result = await sut.ExecuteAsync(new ChangeSubscriptionPlanRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "U1",
            TargetPlanType = "individual_monthly",
            EffectiveWhen = "period_end"
        });

        Assert.True(result.Result);
        Assert.Equal("next_invoice", result.EffectiveWhen);
        Assert.Equal("none", gateway.LastProrationBehavior);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectImmediateYearlyToMonthly()
    {
        var now = Utc(2026, 4, 14);
        var users = new InMemoryUserStore();
        users.Users[("U_PK", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_1",
            StripeSubscriptionId = "sub_1",
            StripePriceId = "price_yearly",
            PlanType = "individual_yearly",
            IndividualPlanTerm = "annual",
            SubscriptionStatus = "active"
        };

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot("sub_1", "cus_1", "active", "price_yearly", "year", 1, false, now.AddMonths(9), null, null)
        };
        var sut = BuildSut(users, gateway, now);

        var result = await sut.ExecuteAsync(new ChangeSubscriptionPlanRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "U1",
            TargetPlanType = "individual_monthly",
            EffectiveWhen = "immediate"
        });

        Assert.False(result.Result);
        Assert.Equal("invalid_effective_when", result.Error);
        Assert.Null(gateway.LastTargetPriceId);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_WhenStripeCustomerDoesNotMatchUser()
    {
        var now = Utc(2026, 4, 14);
        var users = new InMemoryUserStore();
        users.Users[("U_PK", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_user",
            StripeSubscriptionId = "sub_1",
            StripePriceId = "price_monthly",
            PlanType = "individual_monthly",
            SubscriptionStatus = "active"
        };

        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot("sub_1", "cus_other", "active", "price_monthly", "month", 1, false, now.AddDays(20), null, null)
        };
        var sut = BuildSut(users, gateway, now);

        var result = await sut.ExecuteAsync(new ChangeSubscriptionPlanRequest
        {
            ActorUserPk = "U_PK",
            ActorUserId = "U1",
            TargetPlanType = "individual_yearly"
        });

        Assert.False(result.Result);
        Assert.Equal("forbidden", result.Error);
        Assert.Null(gateway.LastTargetPriceId);
    }

    private static ChangeSubscriptionPlanUseCase BuildSut(
        InMemoryUserStore users,
        FakeStripeSubscriptionGateway gateway,
        DateTimeOffset now,
        IAppMetrics? metrics = null)
        => new(
            users,
            gateway,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_monthly",
                IndividualYearlyPriceId = "price_yearly",
                FleetSeatMonthlyPriceId = "price_fleet"
            },
            new FixedClock(now),
            NullLogger<ChangeSubscriptionPlanUseCase>.Instance,
            metrics);

    private static DateTimeOffset Utc(int year, int month, int day)
        => new(year, month, day, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        public Dictionary<(string Pk, string Id), User> Users { get; } = [];

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(Users.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            var existing = Users.Keys.FirstOrDefault(k => k.Id == user.UserId);
            Users[existing == default ? ("U_PK", user.UserId) : existing] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Users.Values.ToArray());

        public Task<StripeUserScanPage> QueryUsersWithStripePageAsync(int take = 500, int startBucket = 0, CancellationToken ct = default)
            => Task.FromResult(new StripeUserScanPage([], 0, 0, 0, true));
    }

    private sealed class FakeStripeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public StripeSubscriptionSnapshot? Current { get; set; }
        public StripeSubscriptionSnapshot? Changed { get; set; }
        public string? LastTargetPriceId { get; private set; }
        public string? LastProrationBehavior { get; private set; }
        public string? LastBillingCycleAnchor { get; private set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(Current);

        public Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(string subscriptionId, string idempotencyKey, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<StripeSubscriptionSnapshot?> ChangeSubscriptionPriceAsync(string subscriptionId, string targetPriceId, string prorationBehavior, string? billingCycleAnchor, string idempotencyKey, CancellationToken ct = default)
        {
            LastTargetPriceId = targetPriceId;
            LastProrationBehavior = prorationBehavior;
            LastBillingCycleAnchor = billingCycleAnchor;
            return Task.FromResult(Changed);
        }
    }

    private sealed class RecordingMetrics : IAppMetrics
    {
        public List<(string Outcome, string Reason, string TargetPlanType, string EffectiveWhen)> SubscriptionPlanChanges { get; } = [];

        public void StripeEventReceived(string eventType) { }
        public void AccessDecisionApplied(string mode, string source) { }
        public void FailedActionQueued(string actionType) { }
        public void FailedActionRetried(string actionType) { }
        public void CompanyJoin(string outcome, string reason) { }
        public void CompanyJoinSeatRefresh(bool refreshed, string reason) { }
        public void ManyChatDispatchQueued(string actionType) { }
        public void ManyChatDispatchProcessed(string actionType, string outcome) { }
        public void SubscriptionPlanChange(string outcome, string reason, string targetPlanType, string effectiveWhen)
            => SubscriptionPlanChanges.Add((outcome, reason, targetPlanType, effectiveWhen));
        public void CompanySeatQuantityChange(string outcome, string reason, string direction, string effectiveWhen) { }
    }
}
