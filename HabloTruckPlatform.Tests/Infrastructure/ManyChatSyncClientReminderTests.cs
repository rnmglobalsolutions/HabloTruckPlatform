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

    private static ManyChatSyncClient BuildClient(RecordingHandler handler)
    {
        var options = new ManyChatOptions
        {
            ApiKey = "test-key",
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty;
            Requests.Add(new CapturedRequest(path, body));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"success\"}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record CapturedRequest(string Path, string Body);
}
