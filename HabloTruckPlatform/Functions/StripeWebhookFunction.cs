using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Stripex;
using HabloTruckPlatform.Infrastructure.Stripe;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class StripeWebhookFunction
{
    private readonly StripeSignatureValidator _sigValidator;
    private readonly StripeEventParser _parser;
    private readonly IStripeEventStore _eventStore;
    private readonly IStripeSubscriptionHandler _subscriptionHandler;
    private readonly IUserResolver _userResolver;
    private readonly Metrics _metrics;
    private readonly ILogger<StripeWebhookFunction> _logger;

    public StripeWebhookFunction(
        StripeSignatureValidator sigValidator,
        StripeEventParser parser,
        IStripeEventStore eventStore,
        IStripeSubscriptionHandler subscriptionHandler,
        IUserResolver userResolver,
        Metrics metrics,
        ILogger<StripeWebhookFunction> logger)
    {
        _sigValidator = sigValidator;
        _parser = parser;
        _eventStore = eventStore;
        _subscriptionHandler = subscriptionHandler;
        _userResolver = userResolver;
        _metrics = metrics;
        _logger = logger;
    }

    [Function("StripeWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/webhook")] HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;

        string json;
        using (var reader = new StreamReader(req.Body))
            json = await reader.ReadToEndAsync();

        var stripeSignature = req.Headers.TryGetValues("Stripe-Signature", out var values)
            ? values.FirstOrDefault()
            : null;

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = _sigValidator.Validate(json, stripeSignature);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe signature");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        _metrics.StripeEventReceived(stripeEvent.Type);

        // Robust createdUtc (avoid SDK type ambiguity)
        var createdToken = stripeEvent.RawJObject?["created"];
        var createdUtc = createdToken != null && long.TryParse(createdToken.ToString(), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime()
            : DateTimeOffset.UtcNow;

        // Idempotency gate (atomic insert-if-not-exists)
        var firstTime = await _eventStore.TryMarkProcessedAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            createdUtc,
            ct);

        if (!firstTime)
        {
            _logger.LogInformation("Duplicate Stripe event ignored: {EventId}", stripeEvent.Id);
            return req.CreateResponse(HttpStatusCode.OK);
        }

        var parsed = _parser.Parse(stripeEvent);

        if (string.IsNullOrWhiteSpace(parsed.Data?.CustomerId))
        {
            _logger.LogInformation("Event without customer ignored: {Type}", parsed.EventType);
            return req.CreateResponse(HttpStatusCode.OK);
        }

        // Inject event metadata (if parser doesn't already stamp)
        parsed.Data!.StripeEventId = stripeEvent.Id;
        parsed.Data.StripeEventCreatedUtc = createdUtc;

        // Optional: logging scope (extra lookup)
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(parsed.Data.CustomerId!, ct);

        using (LogContext.BeginUserScope(_logger, userRef?.UserId, null, parsed.Data.CustomerId))
        {
            try
            {
                await DispatchAsync(parsed, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Stripe event {Type}", parsed.EventType);
                // DO NOT fail webhook → Stripe will retry; idempotency already marked, so just log.
            }
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    private async Task DispatchAsync(StripeParsedEvent parsed, CancellationToken ct)
    {
        switch (parsed.EventType)
        {
            case "checkout.session.completed":
                await _subscriptionHandler.HandleCheckoutCompletedAsync(parsed.Data!, ct);
                break;

            case "invoice.paid":
                await _subscriptionHandler.HandleInvoicePaidAsync(parsed.Data!, ct);
                break;

            case "invoice.payment_failed":
                await _subscriptionHandler.HandleInvoicePaymentFailedAsync(parsed.Data!, ct);
                break;

            case "customer.subscription.updated":
                await _subscriptionHandler.HandleSubscriptionUpdatedAsync(parsed.Data!, ct);
                break;

            case "customer.subscription.deleted":
                await _subscriptionHandler.HandleSubscriptionDeletedAsync(parsed.Data!, ct);
                break;

            default:
                _logger.LogInformation("Unhandled Stripe event type: {Type}", parsed.EventType);
                break;
        }
    }
}