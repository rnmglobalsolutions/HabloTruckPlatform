using System.Diagnostics;
using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Infrastructure.Telemetry;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class StripeReplayFunction
{
    private readonly IStripeAdminClient _stripeAdminClient;
    private readonly IStripeSubscriptionHandler _handler;
    private readonly ILogger<StripeReplayFunction> _logger;
    private readonly IApiKeyValidator _apiKeyValidator;

    public StripeReplayFunction(
        IStripeAdminClient stripeAdminClient,
        IStripeSubscriptionHandler handler,
        IApiKeyValidator apiKeyValidator,
        ILogger<StripeReplayFunction>? logger = null)
    {
        _stripeAdminClient = stripeAdminClient;
        _handler = handler;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger ?? NullLogger<StripeReplayFunction>.Instance;
    }

    [Function("StripeReplayEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/stripe/replay")] HttpRequestData req,
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
            operationName: "stripe_replay",
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            LogContext.Categories.Entry,
            "stripe_replay");

        var body = await JsonSerializer.DeserializeAsync<ReplayRequest>(req.Body, cancellationToken: ctx.CancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.EventId))
        {
            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.ValidationFailed,
                "missing_event_id",
                (int)HttpStatusCode.BadRequest);

            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Missing eventId.");
            return bad;
        }

        using var eventScope = LogContext.BeginOperationScope(
            _logger,
            operationName: "stripe_replay",
            correlationId: correlationId,
            invocationId: invocationId,
            stripeEventId: body.EventId);

        var dependencyWatch = Stopwatch.StartNew();
        var data = await _stripeAdminClient.GetEventDataAsync(body.EventId, ctx.CancellationToken);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
            LogContext.Categories.Dependency,
            "stripe",
            "get_event_data",
            "Stripe API",
            dependencyWatch.ElapsedMilliseconds,
            true,
            data is not null);

        if (data is null)
        {
            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.ValidationFailed,
                "stripe_event_not_found",
                (int)HttpStatusCode.NotFound);

            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteStringAsync("Stripe event not found.");
            return notFound;
        }

        var dispatchWatch = Stopwatch.StartNew();

        switch (body.EventType?.Trim())
        {
            case "checkout.session.completed":
                await _handler.HandleCheckoutCompletedAsync(data, ctx.CancellationToken);
                break;
            case "invoice.paid":
                await _handler.HandleInvoicePaidAsync(data, ctx.CancellationToken);
                break;
            case "invoice.payment_failed":
                await _handler.HandleInvoicePaymentFailedAsync(data, ctx.CancellationToken);
                break;
            case "customer.subscription.updated":
                await _handler.HandleSubscriptionUpdatedAsync(data, ctx.CancellationToken);
                break;
            case "customer.subscription.deleted":
                await _handler.HandleSubscriptionDeletedAsync(data, ctx.CancellationToken);
                break;
            default:
                _logger.LogInformation(
                    "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode}",
                    LogContext.Categories.Outcome,
                    LogContext.Outcomes.ValidationFailed,
                    "unsupported_event_type",
                    (int)HttpStatusCode.BadRequest);

                var badType = req.CreateResponse(HttpStatusCode.BadRequest);
                await badType.WriteStringAsync("Unsupported or missing eventType.");
                return badType;
        }

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} DurationMs={DurationMs} EventType={EventType}",
            LogContext.Categories.Step,
            "dispatch_replay_event",
            dispatchWatch.ElapsedMilliseconds,
            body.EventType);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            LogContext.Outcomes.Completed,
            "replay_executed",
            opWatch.ElapsedMilliseconds);

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteStringAsync("Replay executed.");
        return ok;
    }

    private sealed record ReplayRequest(string EventId, string? EventType);

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


