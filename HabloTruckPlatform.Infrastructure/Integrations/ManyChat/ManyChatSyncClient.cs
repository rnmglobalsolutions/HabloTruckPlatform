using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Infrastructure.Integrations.ManyChat;

public sealed class ManyChatSyncClient : IManyChatSync
{
    private readonly HttpClient _http;
    private readonly ManyChatOptions _opt;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ManyChatSyncClient(HttpClient http, IOptions<ManyChatOptions> opt)
    {
        _http = http;
        _opt = opt.Value;

        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new InvalidOperationException("ManyChat ApiKey is missing.");

        _http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task SyncUserAccessAsync(
        User user, AccessDecision decision, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            return;

        var sid = user.ManyChatSubscriberId!.Trim();

        // 1) Clear access tags
        await RemoveTagByName(sid, _opt.TagAccessFull, ct);
        await RemoveTagByName(sid, _opt.TagAccessGrace, ct);
        await RemoveTagByName(sid, _opt.TagAccessBlocked, ct);

        // 2) Set correct access tag
        var accessTag = decision.Mode switch
        {
            AccessMode.Full => _opt.TagAccessFull,
            AccessMode.Grace => _opt.TagAccessGrace,
            AccessMode.Blocked => _opt.TagAccessBlocked,
            _ => _opt.TagAccessBlocked
        };
        await AddTagByName(sid, accessTag, ct);

        // 3) Clear/Set source tags (Individual/Company)
        await RemoveTagByName(sid, _opt.TagSourceIndividual, ct);
        await RemoveTagByName(sid, _opt.TagSourceCompany, ct);

        if ((decision.Source & AccessSource.Individual) != 0) await AddTagByName(sid, _opt.TagSourceIndividual, ct);
        if ((decision.Source & AccessSource.Company) != 0) await AddTagByName(sid, _opt.TagSourceCompany, ct);

        // 4) Custom fields
        await SetCustomFieldByName(sid, _opt.FieldAccessMode, decision.Mode.ToString(), ct);

        var graceValue = decision.GraceEndsAtUtc is null
            ? ""
            : decision.GraceEndsAtUtc.Value.UtcDateTime.ToString("O");

        await SetCustomFieldByName(sid, _opt.FieldGraceEndsAtUtc, graceValue, ct);

        await SetCustomFieldByName(sid, _opt.FieldCompanyId, user.CompanyId ?? "", ct);
    }

    public async Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.PaymentFailedFlowNs))
            return; // not configured

        var sid = subscriberId.Trim();

        // /fb/sending/sendFlow (flow_ns)
        var payload = new
        {
            subscriber_id = sid,
            flow_ns = _opt.PaymentFailedFlowNs,
            payload = new { }
        };

        await PostJson("fb/sending/sendFlow", payload, ct);
    }

    public async Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
    {
        // Optional hook: If you later want to message an admin subscriber, you’d need admin subscriberId.
        // For now, no-op by design.
        await Task.CompletedTask;
    }

    public async Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
    {
        if (dispatch is null)
            throw new ArgumentNullException(nameof(dispatch));

        if (string.IsNullOrWhiteSpace(dispatch.SubscriberId))
            return;

        var journey = dispatch.Journey?.Trim().ToLowerInvariant() ?? "auto_renew";
        var flowNs = journey == "save_before_churn"
            ? _opt.SaveBeforeChurnFlowNs
            : _opt.RenewalReminderFlowNs;

        if (string.IsNullOrWhiteSpace(flowNs))
            return; // reminder flow not configured

        var payload = new
        {
            subscriber_id = dispatch.SubscriberId.Trim(),
            flow_ns = flowNs,
            payload = new
            {
                reminder_type = dispatch.ReminderType,
                journey = dispatch.Journey,
                days_until_period_end = dispatch.DaysUntilPeriodEnd,
                period_end_utc = dispatch.PeriodEndUtc.UtcDateTime.ToString("O"),
                positive_continuity = dispatch.UsePositiveContinuityFraming,
                reminder_tone = dispatch.ReminderTone,
                template_key = dispatch.TemplateKey,
                audience_segment = dispatch.AudienceSegment,
                subscription_id = dispatch.SubscriptionId,
                user_id = dispatch.UserId,
                company_id = dispatch.CompanyId ?? "",
                is_company_reminder = dispatch.IsCompanyReminder,
                plan_term = dispatch.PlanTerm ?? ""
            }
        };

        await PostJson("fb/sending/sendFlow", payload, ct);
    }

    // --------------------
    // Low-level endpoints
    // --------------------

    private async Task AddTagByName(string subscriberId, string tagName, CancellationToken ct)
    {
        // POST /fb/subscriber/addTagByName
        var payload = new { subscriber_id = subscriberId, tag_name = tagName };
        await PostJson("fb/subscriber/addTagByName", payload, ct);
    }

    private async Task RemoveTagByName(string subscriberId, string tagName, CancellationToken ct)
    {
        // POST /fb/subscriber/removeTagByName
        var payload = new { subscriber_id = subscriberId, tag_name = tagName };
        await PostJson("fb/subscriber/removeTagByName", payload, ct);
    }

    private async Task SetCustomFieldByName(string subscriberId, string fieldName, string value, CancellationToken ct)
    {
        // POST /fb/subscriber/setCustomFieldByName
        var payload = new { subscriber_id = subscriberId, field_name = fieldName, field_value = value };
        await PostJson("fb/subscriber/setCustomFieldByName", payload, ct);
    }

    private async Task PostJson(string path, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await _http.PostAsync(path, content, ct);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"ManyChat call failed: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
        }
    }
}


