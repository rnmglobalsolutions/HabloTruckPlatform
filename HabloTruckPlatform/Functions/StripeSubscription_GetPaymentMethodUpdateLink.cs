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

public sealed class StripeSubscription_GetPaymentMethodUpdateLink
{
    private readonly CreateStripePaymentMethodUpdateLinkUseCase _useCase;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<StripeSubscription_GetPaymentMethodUpdateLink> _logger;

    public StripeSubscription_GetPaymentMethodUpdateLink(
        CreateStripePaymentMethodUpdateLinkUseCase useCase,
        IApiKeyValidator apiKeyValidator,
        ILogger<StripeSubscription_GetPaymentMethodUpdateLink>? logger = null)
    {
        _useCase = useCase;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger ?? NullLogger<StripeSubscription_GetPaymentMethodUpdateLink>.Instance;
    }

    [Function("StripeGetPaymentMethodUpdateLink")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/subscription/payment-method-update-link")] HttpRequestData req,
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
            operationName: "stripe_payment_method_update_link_http",
            correlationId: correlationId,
            invocationId: invocationId);

        StripePaymentMethodUpdateLinkHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<StripePaymentMethodUpdateLinkHttpRequest>(
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

            return await Json(req, HttpStatusCode.BadRequest, new StripePaymentMethodUpdateLinkResult
            {
                Result = false,
                Error = "invalid_json"
            });
        }

        var result = await _useCase.ExecuteAsync(new StripePaymentMethodUpdateLinkRequest
        {
            ActorUserPk = body?.ActorUserPk?.Trim() ?? string.Empty,
            ActorUserId = body?.ActorUserId?.Trim() ?? string.Empty,
            SubscriptionId = string.IsNullOrWhiteSpace(body?.SubscriptionId) ? null : body!.SubscriptionId!.Trim(),
            ReturnUrl = body?.ReturnUrl?.Trim() ?? string.Empty
        }, ctx.CancellationToken);

        var status = result.Result
            ? HttpStatusCode.OK
            : ToStatusCode(result.Error);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} SubscriptionId={SubscriptionId} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            result.Result ? LogContext.Outcomes.Completed : (result.Error == "forbidden" ? LogContext.Outcomes.Denied : LogContext.Outcomes.ValidationFailed),
            result.Error ?? "success",
            (int)status,
            result.SubscriptionId,
            opWatch.ElapsedMilliseconds);

        return await Json(req, status, result);
    }

    private static HttpStatusCode ToStatusCode(string? error)
        => error switch
        {
            "forbidden" => HttpStatusCode.Forbidden,
            "actor_not_found" => HttpStatusCode.NotFound,
            "subscription_not_found" => HttpStatusCode.NotFound,
            "stripe_portal_session_failed" => HttpStatusCode.InternalServerError,
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
