using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Integrations.ManyChat;

public sealed class ManyChatSyncClient : IManyChatSync
{
    private readonly HttpClient _http;
    private readonly ManyChatOptions _opt;
    private readonly ILogger<ManyChatSyncClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ManyChatSyncClient(HttpClient http, IOptions<ManyChatOptions> opt, ILogger<ManyChatSyncClient>? logger = null)
    {
        _http = http;
        _opt = opt.Value;
        _logger = logger ?? NullLogger<ManyChatSyncClient>.Instance;

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
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "manychat_sync_user_access",
                "no_action_needed",
                "missing_subscriber_id");
            return;
        }

        var sid = user.ManyChatSubscriberId!.Trim();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "manychat_sync_user_access",
            ["UserId"] = user.UserId,
            ["CompanyId"] = user.CompanyId,
            ["SeatAssignmentId"] = user.UserId
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} SubscriberIdSuffix={SubscriberIdSuffix} Mode={Mode} Source={Source}",
            "entry",
            "manychat_sync_user_access",
            MaskSubscriberId(sid),
            decision.Mode,
            decision.Source);

        // 1) Clear access tags.
        await RemoveTagByName(sid, _opt.TagAccessFull, ct);
        await RemoveTagByName(sid, _opt.TagAccessGrace, ct);
        await RemoveTagByName(sid, _opt.TagAccessBlocked, ct);

        // 2) Set correct access tag.
        var accessTag = decision.Mode switch
        {
            AccessMode.Full => _opt.TagAccessFull,
            AccessMode.Grace => _opt.TagAccessGrace,
            AccessMode.Blocked => _opt.TagAccessBlocked,
            _ => _opt.TagAccessBlocked
        };
        await AddTagByName(sid, accessTag, ct);

        // 3) Clear/Set source tags (Individual/Company).
        await RemoveTagByName(sid, _opt.TagSourceIndividual, ct);
        await RemoveTagByName(sid, _opt.TagSourceCompany, ct);

        if ((decision.Source & AccessSource.Individual) != 0) await AddTagByName(sid, _opt.TagSourceIndividual, ct);
        if ((decision.Source & AccessSource.Company) != 0) await AddTagByName(sid, _opt.TagSourceCompany, ct);

        // 4) Custom fields.
        await SetCustomFieldByName(sid, _opt.FieldAccessMode, decision.Mode.ToString(), ct);

        var graceValue = decision.GraceEndsAtUtc is null
            ? ""
            : decision.GraceEndsAtUtc.Value.UtcDateTime.ToString("O");

        await SetCustomFieldByName(sid, _opt.FieldGraceEndsAtUtc, graceValue, ct);

        await SetCustomFieldByName(sid, _opt.FieldCompanyId, user.CompanyId ?? "", ct);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason}",
            "outcome",
            "completed",
            "manychat_access_synced");
    }

    public async Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.PaymentFailedFlowNs))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "manychat_trigger_payment_failed",
                "no_action_needed",
                "payment_failed_flow_not_configured");
            return;
        }

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
        // Optional hook: If you later want to message an admin subscriber, you would need admin subscriberId.
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
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} Journey={Journey}",
                "decision",
                "manychat_send_subscription_reminder",
                "no_action_needed",
                "reminder_flow_not_configured",
                journey);
            return; // reminder flow not configured
        }

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

        var watch = Stopwatch.StartNew();
        using var res = await _http.PostAsync(path, content, ct);

        var success = res.IsSuccessStatusCode;

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} StatusCode={StatusCode}",
            "dependency",
            "manychat",
            "http_post",
            path,
            watch.ElapsedMilliseconds,
            success,
            (int)res.StatusCode);

        if (!success)
            throw new HttpRequestException($"ManyChat call failed: {(int)res.StatusCode} {res.ReasonPhrase}.");
    }

    private static string MaskSubscriberId(string subscriberId)
    {
        if (string.IsNullOrWhiteSpace(subscriberId))
            return "";

        var trimmed = subscriberId.Trim();
        return trimmed.Length <= 4 ? trimmed : trimmed[^4..];
    }
}

