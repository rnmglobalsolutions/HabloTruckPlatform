using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
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
    private readonly IStripeEventAuditStore _auditStore;
    private readonly IStripeSubscriptionHandler _subscriptionHandler;
    private readonly IUserResolver _userResolver;
    private readonly Metrics _metrics;
    private readonly ILogger<StripeWebhookFunction> _logger;

    public StripeWebhookFunction(
        StripeSignatureValidator sigValidator,
        StripeEventParser parser,
        IStripeEventStore eventStore,
        IStripeEventAuditStore auditStore,
        IStripeSubscriptionHandler subscriptionHandler,
        IUserResolver userResolver,
        Metrics metrics,
        ILogger<StripeWebhookFunction> logger)
    {
        _sigValidator = sigValidator;
        _parser = parser;
        _eventStore = eventStore;
        _auditStore = auditStore;
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

        var createdToken = stripeEvent.RawJObject?["created"];
        var createdUtc = createdToken != null && long.TryParse(createdToken.ToString(), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime()
            : DateTimeOffset.UtcNow;

        // Idempotency gate
        var firstTime = await _eventStore.TryMarkProcessedAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            createdUtc,
            ct);

        if (!firstTime)
        {
            _logger.LogInformation("Duplicate Stripe event ignored: {EventId}", stripeEvent.Id);

            await AppendAuditSafeAsync(new StripeEventAuditItem(
                StripeEventId: stripeEvent.Id,
                EventType: stripeEvent.Type,
                CustomerId: null,
                SubscriptionId: null,
                PriceId: null,
                Status: null,
                EventCreatedUtc: createdUtc,
                ProcessedUtc: DateTimeOffset.UtcNow,
                Outcome: "ignored_duplicate",
                Reason: "Stripe event already processed",
                UserPk: null,
                UserId: null,
                CurrentPeriodEndUtc: null,
                CancelAtPeriodEnd: null,
                AccessMode: null,
                AccessSource: null,
                Error: null
            ), ct);

            return req.CreateResponse(HttpStatusCode.OK);
        }

        StripeParsedEvent parsed;
        try
        {
            parsed = _parser.Parse(stripeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Stripe event payload. eventId={EventId} eventType={EventType}", stripeEvent.Id, stripeEvent.Type);

            await AppendAuditSafeAsync(new StripeEventAuditItem(
                StripeEventId: stripeEvent.Id,
                EventType: stripeEvent.Type,
                CustomerId: null,
                SubscriptionId: null,
                PriceId: null,
                Status: null,
                EventCreatedUtc: createdUtc,
                ProcessedUtc: DateTimeOffset.UtcNow,
                Outcome: "failed_parse",
                Reason: "Exception while parsing Stripe event payload",
                UserPk: null,
                UserId: null,
                CurrentPeriodEndUtc: null,
                CancelAtPeriodEnd: null,
                AccessMode: null,
                AccessSource: null,
                Error: ex.Message
            ), ct);

            return req.CreateResponse(HttpStatusCode.OK);
        }

        if (string.IsNullOrWhiteSpace(parsed.Data?.CustomerId))
        {
            _logger.LogInformation("Event without customer ignored: {Type}", parsed.EventType);

            await AppendAuditSafeAsync(new StripeEventAuditItem(
                StripeEventId: stripeEvent.Id,
                EventType: parsed.EventType,
                CustomerId: null,
                SubscriptionId: parsed.Data?.SubscriptionId,
                PriceId: parsed.Data?.PriceId,
                Status: parsed.Data?.Status,
                EventCreatedUtc: createdUtc,
                ProcessedUtc: DateTimeOffset.UtcNow,
                Outcome: "ignored_no_customer",
                Reason: "Parsed event had no customer id",
                UserPk: null,
                UserId: null,
                CurrentPeriodEndUtc: parsed.Data?.CurrentPeriodEndUtc,
                CancelAtPeriodEnd: parsed.Data?.CancelAtPeriodEnd,
                AccessMode: null,
                AccessSource: null,
                Error: null
            ), ct);

            return req.CreateResponse(HttpStatusCode.OK);
        }

        // Inject metadata if parser didn't already
        parsed.Data!.StripeEventId = stripeEvent.Id;
        parsed.Data.StripeEventCreatedUtc = createdUtc;

        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(parsed.Data.CustomerId!, ct);

        using (LogContext.BeginUserScope(_logger, userRef?.UserId, null, parsed.Data.CustomerId))
        {
            try
            {
                var dispatch = await DispatchAsync(parsed, ct);

                await AppendAuditSafeAsync(new StripeEventAuditItem(
                    StripeEventId: stripeEvent.Id,
                    EventType: parsed.EventType,
                    CustomerId: parsed.Data.CustomerId,
                    SubscriptionId: parsed.Data.SubscriptionId,
                    PriceId: parsed.Data.PriceId,
                    Status: parsed.Data.Status,
                    EventCreatedUtc: createdUtc,
                    ProcessedUtc: DateTimeOffset.UtcNow,
                    Outcome: dispatch.Outcome,
                    Reason: dispatch.Reason,
                    UserPk: userRef?.UserPk,
                    UserId: userRef?.UserId,
                    CurrentPeriodEndUtc: parsed.Data.CurrentPeriodEndUtc,
                    CancelAtPeriodEnd: parsed.Data.CancelAtPeriodEnd,
                    AccessMode: dispatch.AccessMode,
                    AccessSource: dispatch.AccessSource,
                    Error: null
                ), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Stripe event {Type}", parsed.EventType);

                await AppendAuditSafeAsync(new StripeEventAuditItem(
                    StripeEventId: stripeEvent.Id,
                    EventType: parsed.EventType,
                    CustomerId: parsed.Data.CustomerId,
                    SubscriptionId: parsed.Data.SubscriptionId,
                    PriceId: parsed.Data.PriceId,
                    Status: parsed.Data.Status,
                    EventCreatedUtc: createdUtc,
                    ProcessedUtc: DateTimeOffset.UtcNow,
                    Outcome: "failed",
                    Reason: "Exception while processing Stripe event",
                    UserPk: userRef?.UserPk,
                    UserId: userRef?.UserId,
                    CurrentPeriodEndUtc: parsed.Data.CurrentPeriodEndUtc,
                    CancelAtPeriodEnd: parsed.Data.CancelAtPeriodEnd,
                    AccessMode: null,
                    AccessSource: null,
                    Error: ex.Message
                ), ct);

                // Do not fail webhook
            }
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    private async Task<DispatchResult> DispatchAsync(StripeParsedEvent parsed, CancellationToken ct)
    {
        switch (parsed.EventType)
        {
            case "checkout.session.completed":
                await _subscriptionHandler.HandleCheckoutCompletedAsync(parsed.Data!, ct);
                return new DispatchResult("applied", "Checkout handled", null, null);

            case "invoice.paid":
                {
                    var decision = await _subscriptionHandler.HandleInvoicePaidAsync(parsed.Data!, ct);
                    return ToDispatchResult(decision, "Invoice paid handled");
                }

            case "invoice.payment_failed":
                {
                    var decision = await _subscriptionHandler.HandleInvoicePaymentFailedAsync(parsed.Data!, ct);
                    return ToDispatchResult(decision, "Invoice payment failed handled");
                }

            case "customer.subscription.updated":
                {
                    var decision = await _subscriptionHandler.HandleSubscriptionUpdatedAsync(parsed.Data!, ct);
                    return ToDispatchResult(decision, "Subscription updated handled");
                }

            case "customer.subscription.deleted":
                {
                    var decision = await _subscriptionHandler.HandleSubscriptionDeletedAsync(parsed.Data!, ct);
                    return ToDispatchResult(decision, "Subscription deleted handled");
                }

            default:
                _logger.LogInformation("Unhandled Stripe event type: {Type}", parsed.EventType);
                return new DispatchResult("ignored_unhandled", $"Unhandled Stripe event type: {parsed.EventType}", null, null);
        }
    }

    private static DispatchResult ToDispatchResult(AccessDecision? decision, string defaultReason)
    {
        if (decision is null)
            return new DispatchResult("applied", defaultReason, null, null);

        var reason = decision.Reason ?? defaultReason;

        if (!string.IsNullOrWhiteSpace(reason) &&
            reason.Contains("out-of-order", StringComparison.OrdinalIgnoreCase))
        {
            return new DispatchResult(
                "ignored_out_of_order",
                reason,
                decision.Mode.ToString(),
                decision.Source.ToString());
        }

        return new DispatchResult(
            "applied",
            reason,
            decision.Mode.ToString(),
            decision.Source.ToString());
    }

    private async Task AppendAuditSafeAsync(StripeEventAuditItem item, CancellationToken ct)
    {
        try
        {
            await _auditStore.AppendAsync(item, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append Stripe audit item for event {EventId}", item.StripeEventId);
        }
    }

    private sealed record DispatchResult(
        string Outcome,
        string? Reason,
        string? AccessMode,
        string? AccessSource);
}
