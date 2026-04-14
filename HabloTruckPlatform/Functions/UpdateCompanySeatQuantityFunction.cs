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

public sealed class UpdateCompanySeatQuantityFunction
{
    private readonly UpdateCompanySeatQuantityUseCase _useCase;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<UpdateCompanySeatQuantityFunction> _logger;

    public UpdateCompanySeatQuantityFunction(
        UpdateCompanySeatQuantityUseCase useCase,
        IApiKeyValidator apiKeyValidator,
        ILogger<UpdateCompanySeatQuantityFunction>? logger = null)
    {
        _useCase = useCase;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger ?? NullLogger<UpdateCompanySeatQuantityFunction>.Instance;
    }

    [Function("UpdateCompanySeatQuantity")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/subscription/update-seat-quantity")] HttpRequestData req,
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
            operationName: "company_seat_quantity_change_http",
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} HttpMethod={HttpMethod} Path={Path}",
            LogContext.Categories.Entry,
            "company_seat_quantity_change_http",
            req.Method,
            req.Url.AbsolutePath);

        UpdateCompanySeatQuantityHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<UpdateCompanySeatQuantityHttpRequest>(
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
            operationName: "company_seat_quantity_change_http",
            correlationId: correlationId,
            invocationId: invocationId,
            userId: body?.ActorUserId,
            companyId: body?.CompanyId,
            subscriptionId: body?.SubscriptionId);

        var result = await _useCase.ExecuteAsync(new UpdateCompanySeatQuantityRequest
        {
            ActorUserPk = body?.ActorUserPk?.Trim() ?? string.Empty,
            ActorUserId = body?.ActorUserId?.Trim() ?? string.Empty,
            CompanyId = body?.CompanyId?.Trim(),
            SubscriptionId = string.IsNullOrWhiteSpace(body?.SubscriptionId) ? null : body!.SubscriptionId!.Trim(),
            TargetSeats = body?.TargetSeats ?? 0,
            EffectiveWhen = body?.EffectiveWhen?.Trim()
        }, ctx.CancellationToken);

        var status = result.Result
            ? HttpStatusCode.OK
            : ToStatusCode(result.Error);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} CompanyId={CompanyId} EntitlementId={EntitlementId} SubscriptionId={SubscriptionId} TargetSeats={TargetSeats} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            result.Result ? LogContext.Outcomes.Completed : (result.Error == "forbidden" ? LogContext.Outcomes.Denied : LogContext.Outcomes.ValidationFailed),
            result.Error ?? "success",
            (int)status,
            result.CompanyId,
            result.EntitlementId,
            result.SubscriptionId,
            result.TargetSeats,
            opWatch.ElapsedMilliseconds);

        return await Json(req, status, new
        {
            ok = result.Result,
            companyId = result.CompanyId ?? "",
            entitlementId = result.EntitlementId ?? "",
            subscriptionId = result.SubscriptionId ?? "",
            previousSeatsTotal = result.PreviousSeatsTotal,
            targetSeats = result.TargetSeats,
            seatsUsed = result.SeatsUsed,
            isOverCapacity = result.IsOverCapacity,
            direction = result.Direction ?? "",
            effectiveWhen = result.EffectiveWhen ?? "",
            currentPeriodEndUtc = result.CurrentPeriodEndUtc?.UtcDateTime.ToString("O") ?? "",
            requestedAtUtc = result.RequestedAtUtc.UtcDateTime.ToString("O"),
            error = result.Error
        });
    }

    private static HttpStatusCode ToStatusCode(string? error)
        => error switch
        {
            "forbidden" => HttpStatusCode.Forbidden,
            "actor_not_found" => HttpStatusCode.NotFound,
            "company_not_found" => HttpStatusCode.NotFound,
            "company_entitlement_not_found" => HttpStatusCode.NotFound,
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

