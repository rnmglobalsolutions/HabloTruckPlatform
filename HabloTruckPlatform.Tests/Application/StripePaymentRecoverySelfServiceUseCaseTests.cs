using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripePaymentRecoverySelfServiceUseCaseTests
{
    [Fact]
    public async Task CreatePaymentMethodUpdateLink_Should_ReturnPortalUrl_When_UserOwnsSubscription()
    {
        var now = new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_selfservice_1",
                CustomerId: "cus_selfservice_1",
                Status: "past_due",
                PriceId: "price_ind_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(10),
                CanceledAtUtc: null,
                EndedAtUtc: null)
        };
        var stripeAdmin = new FakeStripeAdminClient
        {
            Session = new StripePaymentMethodUpdateSession(
                SessionId: "bps_123",
                CustomerId: "cus_selfservice_1",
                SubscriptionId: "sub_selfservice_1",
                Url: "https://billing.stripe.com/p/session")
        };

        userStore.Users[("HT_U_100", "U100")] = new User
        {
            UserId = "U100",
            StripeCustomerId = "cus_selfservice_1",
            StripeSubscriptionId = "sub_selfservice_1",
            SubscriptionStatus = "past_due"
        };

        var sut = new CreateStripePaymentMethodUpdateLinkUseCase(
            userStore,
            gateway,
            stripeAdmin,
            new FixedClock(now));

        var result = await sut.ExecuteAsync(new StripePaymentMethodUpdateLinkRequest
        {
            ActorUserPk = "HT_U_100",
            ActorUserId = "U100",
            ReturnUrl = "https://app.hablotruck.com/billing/recovery"
        });

        Assert.True(result.Result);
        Assert.Equal("https://billing.stripe.com/p/session", result.Url);
        Assert.Equal("bps_123", result.SessionId);
        Assert.Equal("cus_selfservice_1", stripeAdmin.LastPortalCustomerId);
        Assert.Equal("sub_selfservice_1", stripeAdmin.LastPortalSubscriptionId);
        Assert.Equal("https://app.hablotruck.com/billing/recovery", stripeAdmin.LastPortalReturnUrl);
        Assert.True(result.ImmediateRetryRecommended);
    }

    [Fact]
    public async Task CreatePaymentMethodUpdateLink_Should_ReturnForbidden_When_SubscriptionBelongsToDifferentCustomer()
    {
        var now = new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_selfservice_2",
                CustomerId: "cus_other",
                Status: "past_due",
                PriceId: "price_ind_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(10),
                CanceledAtUtc: null,
                EndedAtUtc: null)
        };

        userStore.Users[("HT_U_101", "U101")] = new User
        {
            UserId = "U101",
            StripeCustomerId = "cus_selfservice_2",
            StripeSubscriptionId = "sub_selfservice_2"
        };

        var sut = new CreateStripePaymentMethodUpdateLinkUseCase(
            userStore,
            gateway,
            new FakeStripeAdminClient(),
            new FixedClock(now));

        var result = await sut.ExecuteAsync(new StripePaymentMethodUpdateLinkRequest
        {
            ActorUserPk = "HT_U_101",
            ActorUserId = "U101",
            ReturnUrl = "https://app.hablotruck.com/billing/recovery"
        });

        Assert.False(result.Result);
        Assert.Equal("forbidden", result.Error);
    }

    [Fact]
    public async Task RetryOpenInvoice_Should_AttemptPayment_When_OpenInvoiceExists()
    {
        var now = new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_selfservice_3",
                CustomerId: "cus_selfservice_3",
                Status: "past_due",
                PriceId: "price_ind_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(10),
                CanceledAtUtc: null,
                EndedAtUtc: null)
        };
        var stripeAdmin = new FakeStripeAdminClient
        {
            RetryAttempt = new StripeOpenInvoiceRetryAttempt(
                CustomerId: "cus_selfservice_3",
                SubscriptionId: "sub_selfservice_3",
                InvoiceId: "in_123",
                InvoiceStatus: "paid",
                CollectionMethod: "charge_automatically",
                InvoiceFound: true,
                PaymentAttempted: true,
                InvoicePaid: true)
        };

        var manyChat = new RecordingManyChatSync();
        var notifier = new BillingRecoveryManyChatNotifier(
            manyChat,
            new NoopFailedActionStore(),
            new FixedClock(now));

        userStore.Users[("HT_U_102", "U102")] = new User
        {
            UserId = "U102",
            StripeCustomerId = "cus_selfservice_3",
            StripeSubscriptionId = "sub_selfservice_3",
            SubscriptionStatus = "past_due",
            ManyChatSubscriberId = "sid_selfservice_3",
            PaymentRecoveryStartedAtUtc = now.AddHours(-2)
        };

        var sut = new RetryStripeOpenInvoiceUseCase(
            userStore,
            gateway,
            stripeAdmin,
            notifier,
            new FixedClock(now));

        var result = await sut.ExecuteAsync(new StripeRetryOpenInvoiceRequest
        {
            ActorUserPk = "HT_U_102",
            ActorUserId = "U102"
        });

        Assert.True(result.Result);
        Assert.True(result.InvoiceFound);
        Assert.True(result.PaymentAttempted);
        Assert.True(result.InvoicePaid);
        Assert.Equal("in_123", result.InvoiceId);
        Assert.Equal("cus_selfservice_3", stripeAdmin.LastRetryCustomerId);
        Assert.Equal("sub_selfservice_3", stripeAdmin.LastRetrySubscriptionId);
        var update = Assert.Single(manyChat.BillingRecoveryUpdates);
        Assert.Equal(BillingRecoveryManyChatStatuses.PaymentUpdatePendingConfirmation, update.Status);
        Assert.False(update.ActionRequired);
    }

    [Fact]
    public async Task RetryOpenInvoice_Should_ReturnCompletedWithoutAttempt_When_NoOpenInvoiceExists()
    {
        var now = new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_selfservice_4",
                CustomerId: "cus_selfservice_4",
                Status: "past_due",
                PriceId: "price_ind_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(10),
                CanceledAtUtc: null,
                EndedAtUtc: null)
        };
        var stripeAdmin = new FakeStripeAdminClient
        {
            RetryAttempt = new StripeOpenInvoiceRetryAttempt(
                CustomerId: "cus_selfservice_4",
                SubscriptionId: "sub_selfservice_4",
                InvoiceId: null,
                InvoiceStatus: null,
                CollectionMethod: null,
                InvoiceFound: false,
                PaymentAttempted: false,
                InvoicePaid: false)
        };

        var manyChat = new RecordingManyChatSync();
        var notifier = new BillingRecoveryManyChatNotifier(
            manyChat,
            new NoopFailedActionStore(),
            new FixedClock(now));

        userStore.Users[("HT_U_103", "U103")] = new User
        {
            UserId = "U103",
            StripeCustomerId = "cus_selfservice_4",
            StripeSubscriptionId = "sub_selfservice_4",
            SubscriptionStatus = "past_due",
            ManyChatSubscriberId = "sid_selfservice_4",
            PaymentRecoveryStartedAtUtc = now.AddHours(-1)
        };

        var sut = new RetryStripeOpenInvoiceUseCase(
            userStore,
            gateway,
            stripeAdmin,
            notifier,
            new FixedClock(now));

        var result = await sut.ExecuteAsync(new StripeRetryOpenInvoiceRequest
        {
            ActorUserPk = "HT_U_103",
            ActorUserId = "U103"
        });

        Assert.True(result.Result);
        Assert.False(result.InvoiceFound);
        Assert.False(result.PaymentAttempted);
        Assert.False(result.InvoicePaid);
        Assert.Null(result.Error);
        var update = Assert.Single(manyChat.BillingRecoveryUpdates);
        Assert.Equal(BillingRecoveryManyChatStatuses.PaymentUpdatePendingConfirmation, update.Status);
    }

    [Fact]
    public async Task RetryOpenInvoice_Should_NotNotifyManyChat_When_LocalRecoveryIsNotActive()
    {
        var now = new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero);
        var userStore = new InMemoryUserStore();
        var gateway = new FakeStripeSubscriptionGateway
        {
            Current = new StripeSubscriptionSnapshot(
                SubscriptionId: "sub_selfservice_5",
                CustomerId: "cus_selfservice_5",
                Status: "active",
                PriceId: "price_ind_monthly",
                Interval: "month",
                CancelAtPeriodEnd: false,
                CurrentPeriodEndUtc: now.AddDays(10),
                CanceledAtUtc: null,
                EndedAtUtc: null)
        };
        var stripeAdmin = new FakeStripeAdminClient
        {
            RetryAttempt = new StripeOpenInvoiceRetryAttempt(
                CustomerId: "cus_selfservice_5",
                SubscriptionId: "sub_selfservice_5",
                InvoiceId: "in_555",
                InvoiceStatus: "paid",
                CollectionMethod: "charge_automatically",
                InvoiceFound: true,
                PaymentAttempted: true,
                InvoicePaid: true)
        };

        var manyChat = new RecordingManyChatSync();
        var notifier = new BillingRecoveryManyChatNotifier(
            manyChat,
            new NoopFailedActionStore(),
            new FixedClock(now));

        userStore.Users[("HT_U_104", "U104")] = new User
        {
            UserId = "U104",
            StripeCustomerId = "cus_selfservice_5",
            StripeSubscriptionId = "sub_selfservice_5",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_selfservice_5",
            PaymentRecoveryStartedAtUtc = null
        };

        var sut = new RetryStripeOpenInvoiceUseCase(
            userStore,
            gateway,
            stripeAdmin,
            notifier,
            new FixedClock(now));

        var result = await sut.ExecuteAsync(new StripeRetryOpenInvoiceRequest
        {
            ActorUserPk = "HT_U_104",
            ActorUserId = "U104"
        });

        Assert.True(result.Result);
        Assert.True(result.InvoiceFound);
        Assert.True(result.InvoicePaid);
        Assert.Empty(manyChat.BillingRecoveryUpdates);
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        public Dictionary<(string Pk, string Id), User> Users { get; } = new();

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
        {
            Users.TryGetValue((userPk, userId), out var user);
            return Task.FromResult(user);
        }

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            Users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Users.Values.Take(take).ToList());

        public async Task<HabloTruckPlatform.Application.Models.StripeUserScanPage> QueryUsersWithStripePageAsync(int take = 500, int startBucket = 0, CancellationToken ct = default)
        {
            var users = await QueryUsersWithStripeAsync(take, ct);
            return new HabloTruckPlatform.Application.Models.StripeUserScanPage(users, startBucket, startBucket, 0, false);
        }
    }

    private sealed class FakeStripeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public StripeSubscriptionSnapshot? Current { get; set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(Current);

        public Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(string subscriptionId, string idempotencyKey, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeStripeAdminClient : IStripeAdminClient
    {
        public StripePaymentMethodUpdateSession Session { get; set; } = new("bps_default", "cus_default", "sub_default", "https://billing.stripe.com/p/default");
        public StripeOpenInvoiceRetryAttempt RetryAttempt { get; set; } = new("cus_default", "sub_default", null, null, null, false, false, false);
        public string? LastPortalCustomerId { get; private set; }
        public string? LastPortalSubscriptionId { get; private set; }
        public string? LastPortalReturnUrl { get; private set; }
        public string? LastRetryCustomerId { get; private set; }
        public string? LastRetrySubscriptionId { get; private set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult<StripeSubscriptionSnapshot?>(null);

        public Task<StripeEventData?> GetEventDataAsync(string eventId, CancellationToken ct = default)
            => Task.FromResult<StripeEventData?>(null);

        public Task<StripePaymentMethodUpdateSession> CreatePaymentMethodUpdateSessionAsync(string customerId, string? subscriptionId, string returnUrl, CancellationToken ct = default)
        {
            LastPortalCustomerId = customerId;
            LastPortalSubscriptionId = subscriptionId;
            LastPortalReturnUrl = returnUrl;
            return Task.FromResult(Session);
        }

        public Task<StripeOpenInvoiceRetryAttempt> RetryOpenInvoiceAsync(string customerId, string subscriptionId, CancellationToken ct = default)
        {
            LastRetryCustomerId = customerId;
            LastRetrySubscriptionId = subscriptionId;
            return Task.FromResult(RetryAttempt);
        }
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public List<BillingRecoveryManyChatUpdate> BillingRecoveryUpdates { get; } = new();

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SyncBillingRecoveryStatusAsync(BillingRecoveryManyChatUpdate update, CancellationToken ct = default)
        {
            BillingRecoveryUpdates.Add(update);
            return Task.CompletedTask;
        }

        public Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
            => Task.FromResult(new ManyChatResponse { status = "ok" });

        public Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
            => Task.FromResult(new ManyChatResponse { status = "ok" });

        public Task<ManyChatResponse> SetCustomFieldByNameAsync(string subscriberId, string fieldName, string value, CancellationToken ct = default)
            => Task.FromResult(new ManyChatResponse { status = "ok" });
    }

    private sealed class NoopFailedActionStore : IFailedActionStore
    {
        public Task EnsureTableAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FailedActionItem>> GetDueAsync(DateTimeOffset nowUtc, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());
        public Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default) => Task.CompletedTask;
        public Task RescheduleAsync(string pk, string rk, int attempts, DateTimeOffset nextRetryUtc, string lastError, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkDeadAsync(string pk, string rk, int attempts, string lastError, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(DateTimeOffset nowUtc, string status, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());
        public Task RequeueAsync(string pk, string rk, DateTimeOffset nextRetryUtc, CancellationToken ct = default) => Task.CompletedTask;
    }
}
