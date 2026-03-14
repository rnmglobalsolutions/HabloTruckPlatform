using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
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

        await AddTagByNameAsync(sid, _opt.TagAccessFull, ct);
        await RemoveTagByNameAsync(sid, _opt.TagAccessFull, ct);

        await AddTagByNameAsync(sid, _opt.TagAccessGrace, ct);
        await RemoveTagByNameAsync(sid, _opt.TagAccessGrace, ct);

        await AddTagByNameAsync(sid, _opt.TagAccessBlocked, ct);
        await RemoveTagByNameAsync(sid, _opt.TagAccessBlocked, ct);

        var accessTag = decision.Mode switch
        {
            AccessMode.Full => _opt.TagAccessFull,
            AccessMode.Grace => _opt.TagAccessGrace,
            AccessMode.Blocked => _opt.TagAccessBlocked,
            _ => _opt.TagAccessBlocked
        };

        await AddTagByNameAsync(sid, accessTag, ct);

        await AddTagByNameAsync(sid, _opt.TagSourceIndividual, ct);
        await RemoveTagByNameAsync(sid, _opt.TagSourceIndividual, ct);

        await AddTagByNameAsync(sid, _opt.TagSourceCompany, ct);
        await RemoveTagByNameAsync(sid, _opt.TagSourceCompany, ct);

        if ((decision.Source & AccessSource.Individual) != 0)
            await AddTagByNameAsync(sid, _opt.TagSourceIndividual, ct);

        if ((decision.Source & AccessSource.Company) != 0)
            await AddTagByNameAsync(sid, _opt.TagSourceCompany, ct);

        await SetCustomFieldByNameAsync(sid, _opt.FieldAccessMode, decision.Mode.ToString(), ct);

        var graceValue = decision.GraceEndsAtUtc is null
            ? "-"
            : decision.GraceEndsAtUtc.Value.UtcDateTime.ToString("O");

        await SetCustomFieldByNameAsync(sid, _opt.FieldGraceEndsAtUtc, graceValue, ct);
        await SetCustomFieldByNameAsync(sid, _opt.FieldCompanyId, user.CompanyId ?? "-", ct);

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

    public async Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tag_name = tagName
        };

        return await PostJson(_opt.AddTagByNamePath, payload, ct);
    }

    public async Task<ManyChatResponse> RemoveTagByNameAsync(
        string subscriberId, string tagName, CancellationToken ct = default)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            tag_name = tagName
        };

        return await PostJson(_opt.RemoveTagByNamePath, payload, ct);
    }

    public async Task<ManyChatResponse> SetCustomFieldByNameAsync(
        string subscriberId, string fieldName, string value, CancellationToken ct = default)
    {
        var payload = new
        {
            subscriber_id = subscriberId,
            field_name = fieldName,
            field_value = value
        };

        return await PostJson(_opt.SetCustomFieldByNamePath, payload, ct);
    }

    private async Task<ManyChatResponse> PostJson(string path, object payload, CancellationToken ct)
    {
        string json;
        bool success = false;
        try
        {
            json = JsonSerializer.Serialize(payload, JsonOpts);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new ManyChatRequestException(
                path,
                statusCode: null,
                isRetryable: false,
                failureCategory: ManyChatFailureCategory.InvalidPayload,
                message: "ManyChat payload serialization failed.",
                innerException: ex);
        }

        _logger.LogInformation($"ManyChat Path: {_http.BaseAddress}{path} - ManyChat Payload: {json}");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var watch = Stopwatch.StartNew();
        try
        {
            using var res = await _http.PostAsync(path, content, ct);

            success = res.IsSuccessStatusCode;

            LogDependency(path, watch.ElapsedMilliseconds, success, (int)res.StatusCode);

            if (success)
            {
                var result = await res.Content.ReadFromJsonAsync<ManyChatResponse>(JsonOpts, ct);
                if (result is null)
                {
                    // Treat an empty JSON body as an error
                    throw new ManyChatRequestException(
                        path,
                        res.StatusCode,
                        isRetryable: false,
                        ManyChatFailureCategory.Unknown,
                        "ManyChat returned an empty response body.");
                }

                return result;
            }

            throw new ManyChatRequestException(
                path,
                res.StatusCode,
                IsRetryableStatusCode(res.StatusCode),
                ClassifyFailure(res.StatusCode),
                $"ManyChat call failed: {(int)res.StatusCode} {res.ReasonPhrase}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            LogDependency(path, watch.ElapsedMilliseconds, success: false, statusCode: null);

            throw new ManyChatRequestException(
                path,
                statusCode: null,
                isRetryable: true,
                failureCategory: ManyChatFailureCategory.Timeout,
                message: "ManyChat call timed out.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            var isRetryable = ex.StatusCode is null || IsRetryableStatusCode(ex.StatusCode.Value);
            var category = ex.StatusCode is null
                ? ManyChatFailureCategory.Transport
                : ClassifyFailure(ex.StatusCode.Value);

            LogDependency(
                path,
                watch.ElapsedMilliseconds,
                success: false,
                statusCode: ex.StatusCode is null ? null : (int)ex.StatusCode.Value);

            throw new ManyChatRequestException(
                path,
                ex.StatusCode,
                isRetryable,
                category,
                ex.StatusCode is null
                    ? "ManyChat transport failure."
                    : $"ManyChat call failed: {(int)ex.StatusCode.Value} {ex.StatusCode.Value}.",
                ex);
        }
    }

    private void LogDependency(string path, long durationMs, bool success, int? statusCode)
    {
        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} StatusCode={StatusCode}",
            "dependency",
            "manychat",
            "http_post",
            path,
            durationMs,
            success,
            statusCode);
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        // ManyChat retry policy:
        // - retryable: 429, request timeout, and 5xx
        // - non-retryable by default: most 4xx (classified below as permanent)
        if (statusCode == HttpStatusCode.TooManyRequests || statusCode == HttpStatusCode.RequestTimeout)
            return true;

        return (int)statusCode >= 500;
    }

    private static ManyChatFailureCategory ClassifyFailure(HttpStatusCode statusCode)
    {
        if (IsRetryableStatusCode(statusCode))
            return ManyChatFailureCategory.TransientHttp;

        if (statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound)
            return ManyChatFailureCategory.PermanentHttp;

        if ((int)statusCode >= 400 && (int)statusCode < 500)
            return ManyChatFailureCategory.PermanentHttp;

        return ManyChatFailureCategory.Unknown;
    }

    private static string MaskSubscriberId(string subscriberId)
    {
        if (string.IsNullOrWhiteSpace(subscriberId))
            return "";

        var trimmed = subscriberId.Trim();
        return trimmed.Length <= 4 ? trimmed : trimmed[^4..];
    }
}
