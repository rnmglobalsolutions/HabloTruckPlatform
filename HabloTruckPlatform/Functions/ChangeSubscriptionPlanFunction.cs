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

public sealed class ChangeSubscriptionPlanFunction
{
    private readonly ChangeSubscriptionPlanUseCase _useCase;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<ChangeSubscriptionPlanFunction> _logger;

    public ChangeSubscriptionPlanFunction(
        ChangeSubscriptionPlanUseCase useCase,
        IApiKeyValidator apiKeyValidator,
        ILogger<ChangeSubscriptionPlanFunction>? logger = null)
    {
        _useCase = useCase;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger ?? NullLogger<ChangeSubscriptionPlanFunction>.Instance;
    }

    [Function("ChangeSubscriptionPlan")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/subscription/change-plan")] HttpRequestData req,
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
            operationName: "subscription_plan_change_http",
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} HttpMethod={HttpMethod} Path={Path}",
            LogContext.Categories.Entry,
            "subscription_plan_change_http",
            req.Method,
            req.Url.AbsolutePath);

        ChangeSubscriptionPlanHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ChangeSubscriptionPlanHttpRequest>(
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
            operationName: "subscription_plan_change_http",
            correlationId: correlationId,
            invocationId: invocationId,
            userId: body?.ActorUserId,
            subscriptionId: body?.SubscriptionId);

        var result = await _useCase.ExecuteAsync(new ChangeSubscriptionPlanRequest
        {
            ActorUserPk = body?.ActorUserPk?.Trim() ?? string.Empty,
            ActorUserId = body?.ActorUserId?.Trim() ?? string.Empty,
            SubscriptionId = string.IsNullOrWhiteSpace(body?.SubscriptionId) ? null : body!.SubscriptionId!.Trim(),
            TargetPlanType = body?.TargetPlanType?.Trim(),
            EffectiveWhen = body?.EffectiveWhen?.Trim()
        }, ctx.CancellationToken);

        var status = result.Result
            ? HttpStatusCode.OK
            : ToStatusCode(result.Error);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} SubscriptionId={SubscriptionId} TargetPlanType={TargetPlanType} EffectiveWhen={EffectiveWhen} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            result.Result ? LogContext.Outcomes.Completed : (result.Error == "forbidden" ? LogContext.Outcomes.Denied : LogContext.Outcomes.ValidationFailed),
            result.Error ?? "success",
            (int)status,
            result.SubscriptionId,
            result.TargetPlanType,
            result.EffectiveWhen,
            opWatch.ElapsedMilliseconds);

        return await Json(req, status, new
        {
            ok = result.Result,
            subscriptionId = result.SubscriptionId ?? "",
            previousPlanType = result.PreviousPlanType ?? "",
            targetPlanType = result.TargetPlanType ?? "",
            effectiveWhen = result.EffectiveWhen ?? "",
            stripePriceId = result.StripePriceId ?? "",
            interval = result.Interval ?? "",
            subscriptionStatus = result.SubscriptionStatus ?? "",
            currentPeriodEndUtc = result.CurrentPeriodEndUtc?.UtcDateTime.ToString("O") ?? "",
            cancelAtPeriodEnd = result.CancelAtPeriodEnd,
            requestedAtUtc = result.RequestedAtUtc.UtcDateTime.ToString("O"),
            error = result.Error
        });
    }

    private static HttpStatusCode ToStatusCode(string? error)
        => error switch
        {
            "forbidden" => HttpStatusCode.Forbidden,
            "actor_not_found" => HttpStatusCode.NotFound,
            "subscription_not_found" => HttpStatusCode.NotFound,
            "stripe_update_failed" => HttpStatusCode.InternalServerError,
            _ => HttpStatusCode.BadRequest
        };

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

