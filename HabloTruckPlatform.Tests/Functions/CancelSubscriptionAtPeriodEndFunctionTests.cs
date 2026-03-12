using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Functions.Functions;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Domain.Tests.Functions;

public sealed class CancelSubscriptionAtPeriodEndFunctionTests
{
    private const string ValidApiKey = "test-http-api-key";

    [Fact]
    public async Task Run_Should_ReturnOk_ForValidIndividualCancelRequest()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        fixture.UserStore.Users[("HT_U_001", "U1")] = new User
        {
            UserId = "U1",
            StripeCustomerId = "cus_ind_ok",
            StripeSubscriptionId = "sub_ind_ok",
            EmailNormalized = "driver@hablotruck.com"
        };

        fixture.Gateway.Current = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_ind_ok",
            CustomerId: "cus_ind_ok",
            Status: "active",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(20),
            CanceledAtUtc: null,
            EndedAtUtc: null);

        fixture.Gateway.Updated = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_ind_ok",
            CustomerId: "cus_ind_ok",
            Status: "active",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: now.AddDays(20),
            CanceledAtUtc: now,
            EndedAtUtc: null);

        var req = NewRequest("""
{
  "scope": "individual",
  "actorUserPk": "HT_U_001",
  "actorUserId": "U1"
}
""");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("cancelAtPeriodEnd").GetBoolean());
        Assert.Equal("sub_ind_ok", body.GetProperty("subscriptionId").GetString());
    }

    [Fact]
    public async Task Run_Should_ReturnUnauthorized_When_ApiKeyIsMissing()
    {
        var fixture = BuildFixture(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        var req = NewRequest(
            """
{
  "scope": "individual",
  "actorUserPk": "HT_U_001",
  "actorUserId": "U1"
}
""",
            apiKey: null);

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("Unauthorized", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Should_ReturnUnauthorized_When_ApiKeyIsEmpty()
    {
        var fixture = BuildFixture(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        var req = NewRequest(
            """
{
  "scope": "individual",
  "actorUserPk": "HT_U_001",
  "actorUserId": "U1"
}
""",
            apiKey: "   ");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("Unauthorized", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Should_ReturnUnauthorized_When_ApiKeyIsInvalid()
    {
        var fixture = BuildFixture(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        var req = NewRequest(
            """
{
  "scope": "individual",
  "actorUserPk": "HT_U_001",
  "actorUserId": "U1"
}
""",
            apiKey: "invalid-key");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("Unauthorized", body.GetProperty("error").GetString());
    }
    [Fact]
    public async Task Run_Should_ReturnBadRequest_When_RequestJsonIsInvalid()
    {
        var fixture = BuildFixture(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var req = NewRequest("{ invalid_json }");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("Invalid JSON", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Should_ReturnBadRequest_When_UseCaseReturnsInvalidScope()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        fixture.UserStore.Users[("HT_U_002", "U2")] = new User
        {
            UserId = "U2",
            StripeSubscriptionId = "sub_scope_invalid"
        };

        var req = NewRequest("""
{
  "scope": "not-a-real-scope",
  "actorUserPk": "HT_U_002",
  "actorUserId": "U2"
}
""");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_scope", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Should_ReturnForbidden_When_CompanyActorIsUnauthorized()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        fixture.UserStore.Users[("HT_U_003", "U3")] = new User
        {
            UserId = "U3",
            CompanyId = "C_OTHER",
            EmailNormalized = "someone@hablotruck.com"
        };

        fixture.CompanyStore.Companies["C_TARGET"] = new Company
        {
            CompanyId = "C_TARGET",
            AdminEmailNormalized = "admin@fleet.com",
            StripeCustomerId = "cus_company_target"
        };

        var req = NewRequest("""
{
  "scope": "company",
  "actorUserPk": "HT_U_003",
  "actorUserId": "U3",
  "companyId": "C_TARGET",
  "subscriptionId": "sub_company_target"
}
""");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("forbidden", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Run_Should_ReturnInternalServerError_When_StripeScheduleFails()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var fixture = BuildFixture(now);

        fixture.UserStore.Users[("HT_U_004", "U4")] = new User
        {
            UserId = "U4",
            StripeCustomerId = "cus_ind_fail",
            StripeSubscriptionId = "sub_ind_fail",
            EmailNormalized = "driver-fail@hablotruck.com"
        };

        fixture.Gateway.Current = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_ind_fail",
            CustomerId: "cus_ind_fail",
            Status: "active",
            PriceId: "price_ind_monthly",
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(15),
            CanceledAtUtc: null,
            EndedAtUtc: null);
        fixture.Gateway.ThrowOnSchedule = true;

        var req = NewRequest("""
{
  "scope": "individual",
  "actorUserPk": "HT_U_004",
  "actorUserId": "U4"
}
""");

        var response = await fixture.Function.Run(req, req.FunctionContext);
        var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("stripe_update_failed", body.GetProperty("error").GetString());
    }

    private static Fixture BuildFixture(DateTimeOffset now)
    {
        var userStore = new InMemoryUserStore();
        var companyStore = new InMemoryCompanyStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var gateway = new FakeStripeSubscriptionGateway();

        var useCase = new CancelSubscriptionAtPeriodEndUseCase(
            userStore,
            companyStore,
            entitlementStore,
            gateway,
            new FixedClock(now));

        var validator = new ApiKeyValidator(
            Options.Create(new HttpSecurityOptions { HttpApiKey = ValidApiKey }),
            NullLogger<ApiKeyValidator>.Instance);

        var function = new CancelSubscriptionAtPeriodEndFunction(useCase, validator);
        return new Fixture(function, userStore, companyStore, entitlementStore, gateway);
    }

    private static TestHttpRequestData NewRequest(string body, string? apiKey = ValidApiKey)
    {
        var ctx = new TestFunctionContext();
        var req = new TestHttpRequestData(ctx, body);

        if (apiKey is not null)
            req.Headers.Add("x-api-key", apiKey);

        return req;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseData response)
    {
        response.Body.Position = 0;
        using var reader = new StreamReader(response.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var json = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private sealed record Fixture(
        CancelSubscriptionAtPeriodEndFunction Function,
        InMemoryUserStore UserStore,
        InMemoryCompanyStore CompanyStore,
        InMemoryEntitlementStore EntitlementStore,
        FakeStripeSubscriptionGateway Gateway);

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
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _rows = new();

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, entitlementId), out var ent) ? ent : null);

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
    }

    private sealed class FakeStripeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public StripeSubscriptionSnapshot? Current { get; set; }
        public StripeSubscriptionSnapshot? Updated { get; set; }
        public bool ThrowOnSchedule { get; set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(Current);

        public Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(string subscriptionId, string idempotencyKey, CancellationToken ct = default)
        {
            if (ThrowOnSchedule)
                throw new InvalidOperationException("simulated_schedule_failure");

            return Task.FromResult(Updated);
        }
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        public TestHttpRequestData(FunctionContext functionContext, string body)
            : base(functionContext)
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            Headers = new HttpHeadersCollection();
        }

        public override Stream Body { get; }
        public override HttpHeadersCollection Headers { get; }
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();
        public override Uri Url { get; } = new("https://localhost/api/stripe/subscription/cancel-at-period-end");
        public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();
        public override string Method { get; } = "POST";

        public override HttpResponseData CreateResponse()
            => new TestHttpResponseData(FunctionContext);
    }

    private sealed class TestHttpResponseData : HttpResponseData
    {
        public TestHttpResponseData(FunctionContext functionContext)
            : base(functionContext)
        {
            Headers = new HttpHeadersCollection();
            Body = new MemoryStream();
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; }
        public override Stream Body { get; set; }
        public override HttpCookies Cookies { get; } = null!;
    }

    private sealed class TestFunctionContext : FunctionContext
    {
        public override string InvocationId { get; } = Guid.NewGuid().ToString("N");
        public override string FunctionId { get; } = "CancelSubscriptionAtPeriodEnd";
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





