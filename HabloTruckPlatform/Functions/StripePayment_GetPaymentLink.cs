using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace HabloTruckPlatform.Functions.Functions;

public class StripePayment_GetPaymentLink
{
    private readonly StripeCheckoutHandler _stripeCheckoutHandler;
    private readonly ILogger<StripePayment_GetPaymentLink> _logger;
    private readonly IApiKeyValidator _apiKeyValidator;

    public StripePayment_GetPaymentLink(
        StripeCheckoutHandler stripeCheckoutHandler,
        IApiKeyValidator apiKeyValidator,
        ILogger<StripePayment_GetPaymentLink> logger)
    {
        _stripeCheckoutHandler = stripeCheckoutHandler;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("StripeGetPaymentLink")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/payment-link")] HttpRequestData req,
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
            operationName: "stripe_payment_link",
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} HttpMethod={HttpMethod} Path={Path}",
            LogContext.Categories.Entry,
            "stripe_payment_link",
            req.Method,
            req.Url.AbsolutePath);

        StripeCheckoutSessionRequest? input;

        try
        {
            input = await JsonSerializer.DeserializeAsync<StripeCheckoutSessionRequest>(
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
                "invalid_json_body",
                (int)HttpStatusCode.BadRequest,
                opWatch.ElapsedMilliseconds);

            var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
            await badJson.WriteAsJsonAsync(new StripeCheckoutSessionResult
            {
                Result = false,
                Url = "",
                SessionId = null,
                Error = "invalid_json_body"
            }, cancellationToken: ctx.CancellationToken);

            return badJson;
        }

        using var checkoutScope = LogContext.BeginOperationScope(
            _logger,
            operationName: "stripe_payment_link",
            correlationId: correlationId,
            invocationId: invocationId,
            companyId: input?.CompanyId,
            subscriptionId: null);

        try
        {
            var checkoutWatch = Stopwatch.StartNew();
            var result = await _stripeCheckoutHandler.ExecuteAsync(
                input ?? new StripeCheckoutSessionRequest(),
                ctx.CancellationToken);

            _logger.LogDebug(
                "Step completed. LogCategory={LogCategory} Step={Step} DurationMs={DurationMs} Result={Result} Error={Error}",
                LogContext.Categories.Step,
                "checkout_handler_execute",
                checkoutWatch.ElapsedMilliseconds,
                result.Result,
                result.Error);

            var status = result.Result ? HttpStatusCode.OK : HttpStatusCode.BadRequest;

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                result.Result ? LogContext.Outcomes.Completed : LogContext.Outcomes.ValidationFailed,
                result.Result ? "checkout_link_created" : result.Error,
                (int)status,
                opWatch.ElapsedMilliseconds);

            var response = req.CreateResponse(status);
            await response.WriteAsJsonAsync(result, cancellationToken: ctx.CancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "unhandled_exception",
                opWatch.ElapsedMilliseconds);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);

            await errorResponse.WriteAsJsonAsync(new StripeCheckoutSessionResult
            {
                Result = false,
                Url = "",
                SessionId = null,
                Error = "internal_server_error"
            }, cancellationToken: ctx.CancellationToken);

            return errorResponse;
        }
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


