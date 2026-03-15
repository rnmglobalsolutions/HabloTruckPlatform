using System.Net;
using System.Text;
using System.Text.Json;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Integrations.ManyChat;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Domain.Tests.Infrastructure;

public sealed class ManyChatSyncClientReminderTests
{
    [Fact]
    public async Task SendSubscriptionReminderAsync_Should_SendPositiveContinuityMetadata_ForAutoRenewOneDay()
    {
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_auto_1d",
            UserId: "U_AUTO",
            SubscriptionId: "sub_auto",
            ReminderType: "renewal_reminder_1d",
            Journey: "auto_renew",
            DaysUntilPeriodEnd: 1,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: true,
            ReminderTone: "PositiveContinuity",
            TemplateKey: "renewal_positive_continuity_1d",
            AudienceSegment: "active",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly");

        await sut.SendSubscriptionReminderAsync(dispatch);

        var captured = Assert.Single(handler.Requests);
        Assert.Equal("fb/sending/sendFlow", captured.Path);

        using var doc = JsonDocument.Parse(captured.Body);
        var root = doc.RootElement;
        Assert.Equal("flow_renewal", root.GetProperty("flow_ns").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal("renewal_reminder_1d", payload.GetProperty("reminder_type").GetString());
        Assert.Equal("auto_renew", payload.GetProperty("journey").GetString());
        Assert.True(payload.GetProperty("positive_continuity").GetBoolean());
        Assert.Equal("PositiveContinuity", payload.GetProperty("reminder_tone").GetString());
        Assert.Equal("renewal_positive_continuity_1d", payload.GetProperty("template_key").GetString());
    }

    [Fact]
    public async Task SendSubscriptionReminderAsync_Should_SendEndingSoonMetadata_ForCancelScheduledOneDay()
    {
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_cancel_1d",
            UserId: "U_CANCEL",
            SubscriptionId: "sub_cancel",
            ReminderType: "save_before_churn_1d",
            Journey: "save_before_churn",
            DaysUntilPeriodEnd: 1,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: false,
            ReminderTone: "EndingSoonReactivation",
            TemplateKey: "save_before_churn_ending_soon_1d",
            AudienceSegment: "at_risk",
            IsCompanyReminder: true,
            CompanyId: "C_1",
            PlanTerm: "annual");

        await sut.SendSubscriptionReminderAsync(dispatch);

        var captured = Assert.Single(handler.Requests);
        Assert.Equal("fb/sending/sendFlow", captured.Path);

        using var doc = JsonDocument.Parse(captured.Body);
        var root = doc.RootElement;
        Assert.Equal("flow_churn", root.GetProperty("flow_ns").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal("save_before_churn_1d", payload.GetProperty("reminder_type").GetString());
        Assert.Equal("save_before_churn", payload.GetProperty("journey").GetString());
        Assert.False(payload.GetProperty("positive_continuity").GetBoolean());
        Assert.Equal("EndingSoonReactivation", payload.GetProperty("reminder_tone").GetString());
        Assert.Equal("save_before_churn_ending_soon_1d", payload.GetProperty("template_key").GetString());
    }

    [Fact]
    public async Task SendSubscriptionReminderAsync_Should_UsePaymentRecoveryFlow_ForRecoveryFollowup()
    {
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);

        var dispatch = new SubscriptionReminderDispatch(
            SubscriberId: "sid_recovery",
            UserId: "U_RECOVERY",
            SubscriptionId: "sub_recovery",
            ReminderType: "payment_recovery_followup_day_1",
            Journey: "payment_recovery",
            DaysUntilPeriodEnd: 0,
            PeriodEndUtc: new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero),
            UsePositiveContinuityFraming: false,
            ReminderTone: "PaymentRecoveryUpdateMethod",
            TemplateKey: "payment_recovery_followup",
            AudienceSegment: "at_risk",
            IsCompanyReminder: false,
            CompanyId: null,
            PlanTerm: "monthly",
            JourneyDay: 1,
            JourneyAnchorUtc: new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        await sut.SendSubscriptionReminderAsync(dispatch);

        var captured = Assert.Single(handler.Requests);
        using var doc = JsonDocument.Parse(captured.Body);
        var root = doc.RootElement;
        Assert.Equal("flow_payment_recovery", root.GetProperty("flow_ns").GetString());

        var payload = root.GetProperty("payload");
        Assert.Equal("payment_recovery", payload.GetProperty("journey").GetString());
        Assert.Equal(1, payload.GetProperty("journey_day").GetInt32());
        Assert.Equal("payment_recovery_followup", payload.GetProperty("template_key").GetString());
    }

    [Fact]
    public async Task SyncBillingRecoveryStatusAsync_Should_SetFieldsAndActionRequiredTag_ForActiveRecovery()
    {
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);

        await sut.SyncBillingRecoveryStatusAsync(new BillingRecoveryManyChatUpdate(
            SubscriberId: "sid_billing_active",
            UserId: "U_BILLING",
            CompanyId: null,
            SubscriptionId: "sub_billing",
            Status: BillingRecoveryManyChatStatuses.RecoveryActive,
            StatusAtUtc: new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero),
            RecoveryStartedAtUtc: new DateTimeOffset(2026, 3, 14, 14, 0, 0, TimeSpan.Zero),
            InvoiceId: null,
            InvoiceStatus: null,
            ActionRequired: true,
            Recovered: false));

        Assert.Contains(handler.Requests, x => x.Path == "fb/subscriber/setCustomFieldByName" && x.Body.Contains("ht_billing_recovery_status"));
        var last = Assert.Single(handler.Requests.TakeLast(1));
        Assert.Equal("fb/subscriber/addTagByName", last.Path);
        Assert.Contains("HT_BILLING_ACTION_REQUIRED", last.Body);
    }

    [Fact]
    public async Task SyncBillingRecoveryStatusAsync_Should_SetRecoveredTag_ForRecoveredState()
    {
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);

        await sut.SyncBillingRecoveryStatusAsync(new BillingRecoveryManyChatUpdate(
            SubscriberId: "sid_billing_recovered",
            UserId: "U_BILLING_RECOVERED",
            CompanyId: null,
            SubscriptionId: "sub_billing_recovered",
            Status: BillingRecoveryManyChatStatuses.Recovered,
            StatusAtUtc: new DateTimeOffset(2026, 3, 14, 15, 0, 0, TimeSpan.Zero),
            RecoveryStartedAtUtc: new DateTimeOffset(2026, 3, 14, 14, 0, 0, TimeSpan.Zero),
            InvoiceId: null,
            InvoiceStatus: "paid",
            ActionRequired: false,
            Recovered: true));

        var last = Assert.Single(handler.Requests.TakeLast(1));
        Assert.Equal("fb/subscriber/addTagByName", last.Path);
        Assert.Contains("HT_PAYMENT_RECOVERED", last.Body);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowRetryableManyChatRequestException_On503()
    {
        var handler = new RecordingHandler
        {
            StatusCodeToReturn = HttpStatusCode.ServiceUnavailable
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_503"));

        Assert.True(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.TransientHttp, ex.FailureCategory);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowRetryableManyChatRequestException_On429()
    {
        var handler = new RecordingHandler
        {
            StatusCodeToReturn = HttpStatusCode.TooManyRequests
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_429"));

        Assert.True(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.TransientHttp, ex.FailureCategory);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowRetryableManyChatRequestException_On502()
    {
        var handler = new RecordingHandler
        {
            StatusCodeToReturn = HttpStatusCode.BadGateway
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_502"));

        Assert.True(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.TransientHttp, ex.FailureCategory);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowRetryableManyChatRequestException_On504()
    {
        var handler = new RecordingHandler
        {
            StatusCodeToReturn = HttpStatusCode.GatewayTimeout
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_504"));

        Assert.True(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.GatewayTimeout, ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.TransientHttp, ex.FailureCategory);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowRetryableManyChatRequestException_OnTimeout()
    {
        var handler = new RecordingHandler
        {
            ExceptionToThrow = new TaskCanceledException("timeout")
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_timeout"));

        Assert.True(ex.IsRetryable);
        Assert.Null(ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.Timeout, ex.FailureCategory);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowNonRetryableManyChatRequestException_On400()
    {
        var handler = new RecordingHandler
        {
            StatusCodeToReturn = HttpStatusCode.BadRequest
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_400"));

        Assert.False(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.PermanentHttp, ex.FailureCategory);
    }

    [Fact]
    public async Task TriggerPaymentFailedFlowAsync_Should_ThrowRetryableManyChatRequestException_OnTransportFailure()
    {
        var handler = new RecordingHandler
        {
            ExceptionToThrow = new HttpRequestException("network_down")
        };
        var sut = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ManyChatRequestException>(() => sut.TriggerPaymentFailedFlowAsync("sid_transport"));

        Assert.True(ex.IsRetryable);
        Assert.Null(ex.StatusCode);
        Assert.Equal(ManyChatFailureCategory.Transport, ex.FailureCategory);
    }

    [Fact]
    public async Task RemoveTagByName_Should_Get_SuccessResponse()
    {
        var manyChatSubscriberId = "1218185540";
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);
        var result = await sut.RemoveTagByNameAsync(manyChatSubscriberId, "HT_ACCESS_BLOCKED");
        var captured = Assert.Single(handler.Requests);
        Assert.Equal("fb/subscriber/removeTagByName", captured.Path);
        Assert.Equal("success", result?.status);
        Assert.Null(result?.message);
    }

    [Fact]
    public async Task AddTagByName_Should_Get_SuccessResponse()
    {
        var manyChatSubscriberId = "1218185540";
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);
        var result = await sut.AddTagByNameAsync(manyChatSubscriberId, "HT_ACCESS_BLOCKED");
        var captured = Assert.Single(handler.Requests);
        Assert.Equal("fb/subscriber/addTagByName", captured.Path);
        Assert.Equal("success", result?.status);
        Assert.Null(result?.message);
    }

    [Fact]
    public async Task SetCustomFieldByNameAsync_Should_Get_SuccessResponse()
    {
        var manyChatSubscriberId = "1218185540";
        var handler = new RecordingHandler();
        var sut = BuildClient(handler);
        var result = await sut.SetCustomFieldByNameAsync(manyChatSubscriberId, "ht_grace_ends_utc", "");
        var captured = Assert.Single(handler.Requests);
        Assert.Equal("fb/subscriber/setCustomFieldByName", captured.Path);
        Assert.Equal("success", result?.status);
        Assert.Null(result?.message);
    }

    private static ManyChatSyncClient BuildClient(RecordingHandler handler)
    {
        var options = new ManyChatOptions
        {
            ApiKey = "651137388083807:781d2134122f99a37da42f45cf7da219",
            AddTagByNamePath = "fb/subscriber/addTagByName",
            RemoveTagByNamePath = "fb/subscriber/removeTagByName",
            SetCustomFieldByNamePath = "fb/subscriber/setCustomFieldByName",
            SendFlowPath = "fb/sending/sendFlow",
            PaymentFailedFlowNs = "flow_payment_failed",
            PaymentRecoveryReminderFlowNs = "flow_payment_recovery",
            RenewalReminderFlowNs = "flow_renewal",
            SaveBeforeChurnFlowNs = "flow_churn"
        };

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.manychat.com/")
        };

        return new ManyChatSyncClient(http, Options.Create(options));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();
        public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
        public Exception? ExceptionToThrow { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty;
            Requests.Add(new CapturedRequest(path, body));

            return new HttpResponseMessage(StatusCodeToReturn)
            {
                Content = new StringContent("{\"status\":\"success\"}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(string Path, string Body);
}
