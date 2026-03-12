using System.Diagnostics;
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

namespace HabloTruckPlatform.Infrastructure.Integrations.ManyChat;

public sealed class ManyChatSyncClient : IManyChatSync
{
    private readonly HttpClient _http;
    private readonly ManyChatOptions _opt;
    private readonly ILogger<ManyChatSyncClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ManyChatSyncClient(
        HttpClient http,
        IOptions<ManyChatOptions> opt,
        ILogger<ManyChatSyncClient>? logger = null)
    {
        _http = http;
        _opt = opt.Value;
        _logger = logger ?? NullLogger<ManyChatSyncClient>.Instance;

        if (string.IsNullOrWhiteSpace(_opt.ApiKey))
            throw new InvalidOperationException("ManyChat ApiKey is missing.");

        if (string.IsNullOrWhiteSpace(_opt.BaseUrl))
            throw new InvalidOperationException("ManyChat BaseUrl is missing.");

        if (string.IsNullOrWhiteSpace(_opt.AddTagByNamePath))
            throw new InvalidOperationException("ManyChat AddTagByNamePath is missing.");

        if (string.IsNullOrWhiteSpace(_opt.RemoveTagByNamePath))
            throw new InvalidOperationException("ManyChat RemoveTagByNamePath is missing.");

        if (string.IsNullOrWhiteSpace(_opt.SetCustomFieldByNamePath))
            throw new InvalidOperationException("ManyChat SetCustomFieldByNamePath is missing.");

        if (string.IsNullOrWhiteSpace(_opt.SendFlowPath))
            throw new InvalidOperationException("ManyChat SendFlowPath is missing.");

        _http.BaseAddress = new Uri(_opt.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task SyncUserAccessAsync(
        User user,
        AccessDecision decision,
        CancellationToken ct = default)
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

        await RemoveTagByName(sid, _opt.TagAccessFull, ct);
        await RemoveTagByName(sid, _opt.TagAccessGrace, ct);
        await RemoveTagByName(sid, _opt.TagAccessBlocked, ct);

        var accessTag = decision.Mode switch
        {
            AccessMode.Full => _opt.TagAccessFull,
            AccessMode.Grace => _opt.TagAccessGrace,
            AccessMode.Blocked => _opt.TagAccessBlocked,
            _ => _opt.TagAccessBlocked
        };

        await AddTagByName(sid, accessTag, ct);

        await RemoveTagByName(sid, _opt.TagSourceIndividual, ct);
        await RemoveTagByName(sid, _opt.TagSourceCompany, ct);

        if ((decision.Source & AccessSource.Individual) != 0)
            await AddTagByName(sid, _opt.TagSourceIndividual, ct);

        if ((decision.Source & AccessSource.Company) != 0)
            await AddTagByName(sid, _opt.TagSourceCompany, ct);

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

        var payload = new
        {
            subscriber_id = sid,
            flow_ns = _opt.PaymentFailedFlowNs,
            payload = new { }
        };

        await PostJson(_opt.SendFlowPath, payload, ct);
    }

    public async Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
    {
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
            return;
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

        await PostJson(_opt.SendFlowPath, payload, ct);
    }

    private async Task AddTagByName(string subscriberId, string tagName, CancellationToken ct)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tag_name = tagName
        };

        await PostJson(_opt.AddTagByNamePath, payload, ct);
    }

    private async Task RemoveTagByName(string subscriberId, string tagName, CancellationToken ct)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tag_name = tagName
        };

        await PostJson(_opt.RemoveTagByNamePath, payload, ct);
    }

    private async Task SetCustomFieldByName(string subscriberId, string fieldName, string value, CancellationToken ct)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            field_name = fieldName,
            field_value = value
        };

        await PostJson(_opt.SetCustomFieldByNamePath, payload, ct);
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