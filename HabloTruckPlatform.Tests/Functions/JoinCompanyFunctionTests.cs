using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Functions;

public sealed class JoinCompanyFunctionTests
{
    [Fact]
    public async Task Run_Should_NotConsumeInvite_When_ActiveSeatAlreadyExists_ForSameInvite()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var users = new InMemoryUserStore();
        var invites = new InMemoryInviteCodeStore();
        var entitlements = new InMemoryEntitlementStore();
        var seats = new InMemorySeatStore();
        var failedActions = new NoopFailedActionStore();
        var manyChat = new NoopManyChatSync();

        var user = new User
        {
            UserId = "U_JOIN_FN_1",
            ManyChatSubscriberId = "sid_join_fn_1"
        };
        users.Add(user);

        invites.Invites["HT-JOIN1"] = new InviteCode
        {
            Code = "HT-JOIN1",
            CompanyId = "C_JOIN_FN",
            EntitlementId = "E_JOIN_FN",
            Status = "active",
            CreatedAtUtc = clock.UtcNow.AddDays(-1)
        };

        await seats.UpsertAsync(new SeatAssignment
        {
            CompanyId = "C_JOIN_FN",
            UserId = "U_JOIN_FN_1",
            EntitlementId = "E_JOIN_FN",
            Status = "active",
            AssignedAtUtc = clock.UtcNow.AddMinutes(-10)
        });

        await entitlements.UpsertAsync(new Entitlement
        {
            CompanyId = "C_JOIN_FN",
            EntitlementId = "E_JOIN_FN",
            SeatsTotal = 5,
            SeatsUsed = 0,
            IsOverCapacity = false,
            Status = "past_due",
            StartUtc = clock.UtcNow.AddDays(-5),
            UpdatedAtUtc = clock.UtcNow.AddDays(-1)
        });

        var orchestrator = new AccessOrchestrator(
            users,
            seats,
            entitlements,
            manyChat,
            failedActions,
            clock,
            new CompanyGracePolicy(7),
            NullLogger<AccessOrchestrator>.Instance);

        var join = new CompanyJoinHandler(
            users,
            entitlements,
            seats,
            orchestrator,
            clock,
            NullLogger<CompanyJoinHandler>.Instance);

        var function = new global::HabloTruckPlatform.Functions.Functions.JoinCompanyFunction(
            users,
            invites,
            seats,
            join,
            new AllowAllApiKeyValidator(),
            NullLogger<global::HabloTruckPlatform.Functions.Functions.JoinCompanyFunction>.Instance);

        var req = NewRequest("""
{
  "inviteCode": "HT-JOIN1",
  "manyChatSubscriberId": "sid_join_fn_1"
}
""");

        var response = await function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);
        var savedUser = await users.GetAsync(Buckets.UserBucketPk(user.UserId), user.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("alreadyJoined").GetBoolean());
        Assert.Equal(0, invites.TryConsumeCount);
        Assert.NotNull(savedUser);
        Assert.Equal("C_JOIN_FN", savedUser!.CompanyId);
        Assert.Equal("E_JOIN_FN", savedUser.SeatEntitlementId);
    }

    private static TestHttpRequestData NewRequest(string body)
    {
        var ctx = new TestFunctionContext();
        var req = new TestHttpRequestData(ctx, body);
        req.Headers.Add("x-api-key", "valid");
        return req;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(response.Body);
        return doc.RootElement.Clone();
    }

    private sealed class AllowAllApiKeyValidator : IApiKeyValidator
    {
        public ApiKeyValidationResult Validate(string? presentedApiKey)
            => ApiKeyValidationResult.Valid;
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _rows = new();

        public void Add(User user)
            => _rows[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            _rows[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
        {
            var existing = _rows.Values.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(manyChatSubscriberId)
                && string.Equals(x.ManyChatSubscriberId, manyChatSubscriberId, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
                return Task.FromResult(existing);

            throw new InvalidOperationException("Test expected an existing user.");
        }

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(_rows.Values.Take(take).ToList());

        public async Task<HabloTruckPlatform.Application.Models.StripeUserScanPage> QueryUsersWithStripePageAsync(int take = 500, int startBucket = 0, CancellationToken ct = default)
        {
            var users = await QueryUsersWithStripeAsync(take, ct);
            return new HabloTruckPlatform.Application.Models.StripeUserScanPage(users, startBucket, startBucket, 0, false);
        }
    }

    private sealed class InMemoryInviteCodeStore : IInviteCodeStore
    {
        public Dictionary<string, InviteCode> Invites { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int TryConsumeCount { get; private set; }

        public Task EnsureTableAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<InviteCode?> GetAsync(string code, CancellationToken ct = default)
            => Task.FromResult(Invites.TryGetValue(code, out var invite) ? invite : null);

        public Task CreateAsync(InviteCode invite, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertAsync(InviteCode invite, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> TryConsumeAsync(string code, DateTimeOffset nowUtc, CancellationToken ct = default)
        {
            TryConsumeCount++;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<InviteCode>> ListForCompanyAsync(string companyId, int take = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InviteCode>>(Array.Empty<InviteCode>());
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _rows = new();

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, entitlementId), out var entitlement) ? entitlement : null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<SeatReservationResult> TryReserveSeatAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(new SeatReservationResult(SeatReservationOutcome.NoCapacity, null));

        public Task<Entitlement?> SyncSeatsUsedAsync(string companyId, string entitlementId, int seatsUsedFloor, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((companyId, entitlementId), out var entitlement))
            {
                entitlement.SeatsUsed = Math.Max(entitlement.SeatsUsed, seatsUsedFloor);
                entitlement.IsOverCapacity = entitlement.SeatsUsed > entitlement.SeatsTotal;
                return Task.FromResult<Entitlement?>(entitlement);
            }

            return Task.FromResult<Entitlement?>(null);
        }

        public Task ReleaseSeatReservationAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemorySeatStore : ISeatAssignmentStore
    {
        private readonly Dictionary<(string CompanyId, string UserId), SeatAssignment> _rows = new();

        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, userId), out var seat) ? seat : null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
        {
            _rows[(seat.CompanyId, seat.UserId)] = seat;
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.Values.Count(x =>
                string.Equals(x.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EntitlementId, entitlementId, StringComparison.OrdinalIgnoreCase)
                && x.IsActive()));
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

    private sealed class NoopManyChatSync : IManyChatSync
    {
        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncBillingRecoveryStatusAsync(BillingRecoveryManyChatUpdate update, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default) => Task.FromResult(new ManyChatResponse());
        public Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default) => Task.FromResult(new ManyChatResponse());
        public Task<ManyChatResponse> SetCustomFieldByNameAsync(string subscriberId, string fieldName, string value, CancellationToken ct = default) => Task.FromResult(new ManyChatResponse());
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        public TestHttpRequestData(FunctionContext functionContext, string body) : base(functionContext)
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            Headers = new HttpHeadersCollection();
            Url = new Uri("https://localhost/api/company/join");
            Identities = Array.Empty<ClaimsIdentity>();
            Cookies = Array.Empty<IHttpCookie>();
            Method = "POST";
        }

        public override Stream Body { get; }
        public override HttpHeadersCollection Headers { get; }
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; }
        public override Uri Url { get; }
        public override IEnumerable<ClaimsIdentity> Identities { get; }
        public override string Method { get; }
        public override HttpResponseData CreateResponse() => new TestHttpResponseData(FunctionContext);
    }

    private sealed class TestHttpResponseData : HttpResponseData
    {
        public TestHttpResponseData(FunctionContext functionContext) : base(functionContext)
        {
            Body = new MemoryStream();
            Headers = new HttpHeadersCollection();
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; }
        public override Stream Body { get; set; }
        public override HttpCookies Cookies => null!;
    }

    private sealed class TestFunctionContext : FunctionContext
    {
        public override string InvocationId { get; } = Guid.NewGuid().ToString("N");
        public override string FunctionId { get; } = "JoinCompany";
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
