using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Functions.Timers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Functions;

public sealed class SubscriptionReminderTimerFunctionTests
{
    [Fact]
    public async Task Run_Should_InvokeReminderService_AndSendDueReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 13, 30, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        fixture.UserStore.Users.Add(new User
        {
            UserId = "U_timer_due",
            StripeSubscriptionId = "sub_timer_due",
            StripeCustomerId = "cus_timer_due",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(1),
            ManyChatSubscriberId = "sid_timer_due",
            PlanType = "individual_monthly"
        });

        var timer = new TimerInfo
        {
            IsPastDue = false,
            ScheduleStatus = new ScheduleStatus()
        };

        await fixture.Function.Run(timer, new TestFunctionContext());

        Assert.Single(fixture.ManyChat.Dispatches);
        Assert.Equal("renewal_reminder_1d", fixture.ManyChat.Dispatches[0].ReminderType);
        Assert.True(fixture.ManyChat.Dispatches[0].UsePositiveContinuityFraming);
    }

    [Fact]
    public async Task Run_Should_BeIdempotent_OnRepeatedTimerInvocationsSameWindow()
    {
        var now = new DateTimeOffset(2026, 3, 10, 13, 30, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        fixture.UserStore.Users.Add(new User
        {
            UserId = "U_timer_once",
            StripeSubscriptionId = "sub_timer_once",
            StripeCustomerId = "cus_timer_once",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            ManyChatSubscriberId = "sid_timer_once",
            PlanType = "individual_monthly"
        });

        var timer = new TimerInfo
        {
            IsPastDue = true,
            ScheduleStatus = new ScheduleStatus()
        };

        await fixture.Function.Run(timer, new TestFunctionContext());
        await fixture.Function.Run(timer, new TestFunctionContext());

        Assert.Single(fixture.ManyChat.Dispatches);
        Assert.Equal("renewal_reminder_7d", fixture.ManyChat.Dispatches[0].ReminderType);
    }

    private static Fixture BuildFixture(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        var users = new InMemoryUserStore();
        var companies = new InMemoryCompanyStore();
        var reminders = new InMemoryReminderStore();
        var manyChat = new RecordingManyChatSync();
        var failedActions = new NoopFailedActionStore();

        var service = new SubscriptionReminderService(
            users,
            companies,
            reminders,
            manyChat,
            failedActions,
            clock,
            NullLogger<SubscriptionReminderService>.Instance);

        var function = new SubscriptionReminderTimerFunction(
            service,
            NullLogger<SubscriptionReminderTimerFunction>.Instance);

        return new Fixture(function, users, manyChat);
    }

    private sealed record Fixture(
        SubscriptionReminderTimerFunction Function,
        InMemoryUserStore UserStore,
        RecordingManyChatSync ManyChat);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        public List<User> Users { get; } = new();

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(Users.FirstOrDefault(x => x.UserId == userId));

        public Task UpsertAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Users.Take(take).ToList());
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
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> TryMarkSentAsync(string subscriptionId, string reminderType, DateTimeOffset periodEndUtc, DateTimeOffset sentAtUtc, CancellationToken ct = default)
        {
            var key = $"{subscriptionId.Trim()}|{reminderType.Trim().ToLowerInvariant()}|{periodEndUtc.UtcDateTime:yyyyMMdd}";
            return Task.FromResult(_keys.Add(key));
        }
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public List<SubscriptionReminderDispatch> Dispatches { get; } = new();

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
        {
            Dispatches.Add(dispatch);
            return Task.CompletedTask;
        }
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

    private sealed class TestFunctionContext : FunctionContext
    {
        public override string InvocationId { get; } = Guid.NewGuid().ToString("N");
        public override string FunctionId { get; } = "SubscriptionReminderTimer";
        public override TraceContext TraceContext { get; } = null!;
        public override BindingContext BindingContext { get; } = null!;
        public override RetryContext RetryContext { get; } = null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition { get; } = null!;
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features { get; } = null!;
        public override CancellationToken CancellationToken { get; } = CancellationToken.None;
    }
}

