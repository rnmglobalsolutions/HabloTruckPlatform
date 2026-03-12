using System.Diagnostics;
using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Infrastructure.Telemetry;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class CancelSubscriptionAtPeriodEndFunction
{
    private readonly CancelSubscriptionAtPeriodEndUseCase _useCase;
    private readonly ILogger<CancelSubscriptionAtPeriodEndFunction> _logger;
    private readonly IApiKeyValidator _apiKeyValidator;

    public CancelSubscriptionAtPeriodEndFunction(
        CancelSubscriptionAtPeriodEndUseCase useCase,
        IApiKeyValidator apiKeyValidator,
        ILogger<CancelSubscriptionAtPeriodEndFunction>? logger = null)
    {
        _useCase = useCase;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger ?? NullLogger<CancelSubscriptionAtPeriodEndFunction>.Instance;
    }

    [Function("CancelSubscriptionAtPeriodEnd")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/subscription/cancel-at-period-end")] HttpRequestData req,
        FunctionContext ctx)
    {
        var unauthorized = await ApiKeyAuthorizationHelper.AuthorizeAsync(
            req,
            _apiKeyValidator,
            _logger,
            ctx.CancellationToken);

        if (unauthorized is not null)
            return unauthorized;

        var invocationId = ctx.InvocationId;
        var correlationId = LogContext.ResolveCorrelationId(
            FirstHeader(req, "x-correlation-id", "x-request-id"),
            invocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "cancel_subscription_at_period_end_http",
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} HttpMethod={HttpMethod} Path={Path}",
            LogContext.Categories.Entry,
            "cancel_subscription_at_period_end_http",
            req.Method,
            req.Url.AbsolutePath);

        CancelSubscriptionAtPeriodEndHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CancelSubscriptionAtPeriodEndHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ctx.CancellationToken);
        }
        catch
        {
            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.ValidationFailed,
                "invalid_json",
                (int)HttpStatusCode.BadRequest,
                opWatch.ElapsedMilliseconds);

            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        using var businessScope = LogContext.BeginOperationScope(
            _logger,
            operationName: "cancel_subscription_at_period_end_http",
            correlationId: correlationId,
            invocationId: invocationId,
            userId: body?.ActorUserId,
            companyId: body?.CompanyId,
            subscriptionId: body?.SubscriptionId);

        var useCaseWatch = Stopwatch.StartNew();
        var result = await _useCase.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = body?.Scope ?? "individual",
            ActorUserPk = body?.ActorUserPk?.Trim() ?? string.Empty,
            ActorUserId = body?.ActorUserId?.Trim() ?? string.Empty,
            CompanyId = string.IsNullOrWhiteSpace(body?.CompanyId) ? null : body!.CompanyId!.Trim(),
            SubscriptionId = string.IsNullOrWhiteSpace(body?.SubscriptionId) ? null : body!.SubscriptionId!.Trim()
        }, ctx.CancellationToken);

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} DurationMs={DurationMs} Result={Result} Error={Error}",
            LogContext.Categories.Step,
            "cancel_subscription_usecase",
            useCaseWatch.ElapsedMilliseconds,
            result.Result,
            result.Error);

        var status = result.Result
            ? HttpStatusCode.OK
            : ToStatusCode(result.Error);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} Scope={Scope} SubscriptionId={SubscriptionId} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            result.Result ? LogContext.Outcomes.Completed : (result.Error == "forbidden" ? LogContext.Outcomes.Denied : LogContext.Outcomes.ValidationFailed),
            result.Error ?? "success",
            (int)status,
            result.Scope,
            result.SubscriptionId,
            opWatch.ElapsedMilliseconds);

        return await Json(req, status, new
        {
            ok = result.Result,
            scope = result.Scope,
            subscriptionId = result.SubscriptionId ?? "",
            cancelAtPeriodEnd = result.CancelAtPeriodEnd,
            alreadyScheduled = result.AlreadyScheduled,
            effectivePeriodEndUtc = result.EffectivePeriodEndUtc?.UtcDateTime.ToString("O") ?? "",
            currentPeriodEndUtc = result.CurrentPeriodEndUtc?.UtcDateTime.ToString("O") ?? "",
            cancelRequestedAtUtc = result.CancelRequestedAtUtc.UtcDateTime.ToString("O"),
            canceledAtUtc = result.CanceledAtUtc?.UtcDateTime.ToString("O") ?? "",
            error = result.Error
        });
    }

    private static HttpStatusCode ToStatusCode(string? error)
    {
        return error switch
        {
            "forbidden" => HttpStatusCode.Forbidden,
            "actor_not_found" => HttpStatusCode.NotFound,
            "company_not_found" => HttpStatusCode.NotFound,
            "company_entitlement_not_found" => HttpStatusCode.NotFound,
            "subscription_not_found" => HttpStatusCode.NotFound,
            "stripe_update_failed" => HttpStatusCode.InternalServerError,
            _ => HttpStatusCode.BadRequest
        };
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }

    private static string? FirstHeader(HttpRequestData req, params string[] names)
    {
        foreach (var name in names)
        {
            if (req.Headers.TryGetValues(name, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
        }

        return null;
    }
}


