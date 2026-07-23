using System.Diagnostics;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Net;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class StripeWebhookFunction
{
    private const string OperationName = "stripe_webhook";

    private readonly StripeSignatureValidator _sigValidator;
    private readonly StripeEventParser _parser;
    private readonly IStripeEventStore _eventStore;
    private readonly IStripeEventAuditStore _auditStore;
    private readonly IStripeSubscriptionHandler _subscriptionHandler;
    private readonly IUserResolver _userResolver;
    private readonly IAdminPaymentAlertNotifier? _adminPaymentAlerts;
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
        ILogger<StripeWebhookFunction> logger,
        IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        _sigValidator = sigValidator;
        _parser = parser;
        _eventStore = eventStore;
        _auditStore = auditStore;
        _subscriptionHandler = subscriptionHandler;
        _userResolver = userResolver;
        _adminPaymentAlerts = adminPaymentAlerts;
        _metrics = metrics;
        _logger = logger;
    }

    [Function("StripeWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/webhook")] HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var invocationId = ctx.InvocationId;
        var correlationId = LogContext.ResolveCorrelationId(
            FirstHeader(req, "x-correlation-id", "x-request-id"),
            invocationId);
        var started = Stopwatch.StartNew();

        using var operationScope = LogContext.BeginOperationScope(
            _logger,
            operationName: OperationName,
            correlationId: correlationId,
            invocationId: invocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} HttpMethod={HttpMethod} Path={Path}",
            LogContext.Categories.Entry,
            req.Method,
            req.Url.AbsolutePath);

        string json;
        using (var reader = new StreamReader(req.Body))
        {
            json = await reader.ReadToEndAsync(ct);
        }

        _logger.LogDebug(
            "Webhook payload accepted. LogCategory={LogCategory} Step={Step} PayloadBytes={PayloadBytes}",
            LogContext.Categories.Step,
            "read_body",
            json.Length);

        var stripeSignature = FirstHeader(req, "Stripe-Signature");

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = _sigValidator.Validate(json, stripeSignature);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stripe signature validation failed. LogCategory={LogCategory} Outcome={Outcome}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.ValidationFailed);

            return BuildOutcomeResponse(
                req,
                HttpStatusCode.BadRequest,
                LogContext.Outcomes.ValidationFailed,
                "invalid_signature",
                started.ElapsedMilliseconds);
        }

        using var eventScope = LogContext.BeginOperationScope(
            _logger,
            operationName: OperationName,
            correlationId: correlationId,
            invocationId: invocationId,
            stripeEventId: stripeEvent.Id);

        _metrics.StripeEventReceived(stripeEvent.Type);

        _logger.LogDebug(
            "Stripe event received. LogCategory={LogCategory} EventType={EventType}",
            LogContext.Categories.Step,
            stripeEvent.Type);

        var createdToken = stripeEvent.RawJObject?["created"];
        var createdUtc = createdToken != null && long.TryParse(createdToken.ToString(), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime()
            : DateTimeOffset.UtcNow;

        var idempotencyWatch = Stopwatch.StartNew();
        var processingStart = await _eventStore.TryStartProcessingAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            createdUtc,
            ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} FirstTime={FirstTime}",
            LogContext.Categories.Dependency,
            "table_storage",
            "stripe_events.try_mark_processed",
            "StripeEvents",
            idempotencyWatch.ElapsedMilliseconds,
            true,
            processingStart);

        if (processingStart == StripeEventProcessingStartResult.Started)
        {
            _logger.LogInformation(
                "Persistence transition. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Persistence,
                LogContext.Outcomes.Applied,
                "processing_lease_acquired",
                idempotencyWatch.ElapsedMilliseconds);
        }

        if (processingStart == StripeEventProcessingStartResult.AlreadyInProgress)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                LogContext.Categories.Decision,
                "event_processing_in_progress",
                LogContext.Outcomes.SkippedDuplicate,
                "Stripe event is already being processed");

            return BuildOutcomeResponse(
                req,
                HttpStatusCode.OK,
                LogContext.Outcomes.SkippedDuplicate,
                "event_in_progress",
                started.ElapsedMilliseconds);
        }

        if (processingStart == StripeEventProcessingStartResult.AlreadyProcessed)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                LogContext.Categories.Decision,
                "duplicate_event_skipped",
                LogContext.Outcomes.SkippedDuplicate,
                "Stripe event already processed");

            await AppendAuditSafeAsync(new StripeEventAuditItem(
                StripeEventId: stripeEvent.Id,
                EventType: stripeEvent.Type,
                CustomerId: null,
                SubscriptionId: null,
                PriceId: null,
                Status: null,
                EventCreatedUtc: createdUtc,
                ProcessedUtc: DateTimeOffset.UtcNow,
                Outcome: "skipped_duplicate",
                Reason: "Stripe event already processed",
                UserPk: null,
                UserId: null,
                CurrentPeriodEndUtc: null,
                CancelAtPeriodEnd: null,
                AccessMode: null,
                AccessSource: null,
                Error: null
            ), ct);

            return BuildOutcomeResponse(
                req,
                HttpStatusCode.OK,
                LogContext.Outcomes.SkippedDuplicate,
                "duplicate_event",
                started.ElapsedMilliseconds);
        }

        StripeParsedEvent parsed;
        try
        {
            parsed = _parser.Parse(stripeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Stripe event parse failed. LogCategory={LogCategory} Outcome={Outcome} EventType={EventType}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.ValidationFailed,
                stripeEvent.Type);

            if (IsPaymentFailureEvent(stripeEvent.Type))
            {
                await NotifyUnhandledPaymentFailureAsync(
                    stripeEvent,
                    createdUtc,
                    "payment_failure_event_parse_failed",
                    ct);
            }

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

            await _eventStore.MarkProcessedAsync(stripeEvent.Id, ct);

            return BuildOutcomeResponse(
                req,
                HttpStatusCode.OK,
                LogContext.Outcomes.ValidationFailed,
                "failed_parse",
                started.ElapsedMilliseconds);
        }

        if (string.IsNullOrWhiteSpace(parsed.Data?.CustomerId))
        {
            if (IsPaymentFailureEvent(parsed.EventType))
            {
                await NotifyUnhandledPaymentFailureAsync(
                    stripeEvent,
                    createdUtc,
                    parsed.Data is null ? "unhandled_payment_failure_event" : "payment_failure_event_customer_missing",
                    ct);
            }

            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} EventType={EventType}",
                LogContext.Categories.Decision,
                "skipped_without_customer",
                LogContext.Outcomes.SkippedNoCustomer,
                parsed.EventType);

            await AppendAuditSafeAsync(new StripeEventAuditItem(
                StripeEventId: stripeEvent.Id,
                EventType: parsed.EventType,
                CustomerId: null,
                SubscriptionId: parsed.Data?.SubscriptionId,
                PriceId: parsed.Data?.PriceId,
                Status: parsed.Data?.Status,
                EventCreatedUtc: createdUtc,
                ProcessedUtc: DateTimeOffset.UtcNow,
                Outcome: "skipped_no_customer",
                Reason: "Parsed event had no customer id",
                UserPk: null,
                UserId: null,
                CurrentPeriodEndUtc: parsed.Data?.CurrentPeriodEndUtc,
                CancelAtPeriodEnd: parsed.Data?.CancelAtPeriodEnd,
                AccessMode: null,
                AccessSource: null,
                Error: null
            ), ct);

            await _eventStore.MarkProcessedAsync(stripeEvent.Id, ct);

            return BuildOutcomeResponse(
                req,
                HttpStatusCode.OK,
                LogContext.Outcomes.SkippedNoCustomer,
                "customer_missing",
                started.ElapsedMilliseconds);
        }

        // Inject metadata if parser didn't already.
        parsed.Data!.StripeEventId = stripeEvent.Id;
        parsed.Data.StripeEventCreatedUtc = createdUtc;

        var resolveUserWatch = Stopwatch.StartNew();
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(parsed.Data.CustomerId!, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
            LogContext.Categories.Dependency,
            "table_storage",
            "user_resolver.resolve_by_stripe_customer_id",
            "UserStripeCustomerLookup",
            resolveUserWatch.ElapsedMilliseconds,
            true,
            userRef is not null);

        using var processingScope = LogContext.BeginOperationScope(
            _logger,
            operationName: OperationName,
            correlationId: correlationId,
            invocationId: invocationId,
            stripeEventId: stripeEvent.Id,
            userId: userRef?.UserId,
            stripeCustomerId: parsed.Data.CustomerId,
            subscriptionId: parsed.Data.SubscriptionId);

        DispatchResult? dispatch = null;
        try
        {
            var dispatchWatch = Stopwatch.StartNew();
            dispatch = await DispatchAsync(parsed, ct);
            await _eventStore.MarkProcessedAsync(stripeEvent.Id, ct);

            _logger.LogDebug(
                "Step completed. LogCategory={LogCategory} Step={Step} DispatchOutcome={DispatchOutcome} DurationMs={DurationMs} Reason={Reason}",
                LogContext.Categories.Step,
                "dispatch_event",
                dispatch.Outcome,
                dispatchWatch.ElapsedMilliseconds,
                dispatch.Reason);

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
            await _eventStore.ReleaseProcessingAsync(stripeEvent.Id, ct);

            _logger.LogError(
                ex,
                "Stripe event processing failed. LogCategory={LogCategory} Outcome={Outcome} EventType={EventType}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                parsed.EventType);

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

            return BuildOutcomeResponse(
                req,
                HttpStatusCode.InternalServerError,
                LogContext.Outcomes.DependencyFailed,
                "handler_exception",
                started.ElapsedMilliseconds);
        }

        return BuildOutcomeResponse(
            req,
            HttpStatusCode.OK,
            NormalizeOutcomeForLogs(dispatch?.Outcome),
            dispatch?.Reason ?? "processed",
            started.ElapsedMilliseconds);
    }

    private async Task<DispatchResult> DispatchAsync(StripeParsedEvent parsed, CancellationToken ct)
    {
        _logger.LogDebug(
            "Dispatch started. LogCategory={LogCategory} Step={Step} EventType={EventType}",
            LogContext.Categories.Step,
            "dispatch",
            parsed.EventType);

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

            case "customer.updated":
                await _subscriptionHandler.HandleCustomerUpdatedAsync(parsed.Data!, ct);
                return new DispatchResult("applied", "Customer updated handled", null, null);

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
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} EventType={EventType}",
                    LogContext.Categories.Decision,
                    "unhandled_event_type",
                    LogContext.Outcomes.SkippedUnhandled,
                    parsed.EventType);

                return new DispatchResult(
                    "skipped_unhandled",
                    $"Unhandled Stripe event type: {parsed.EventType}",
                    null,
                    null);
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
                "skipped_out_of_order",
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
        var auditWatch = Stopwatch.StartNew();
        try
        {
            await _auditStore.AppendAsync(item, ct);

            _logger.LogDebug(
                "Persistence completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success} AuditOutcome={AuditOutcome}",
                LogContext.Categories.Persistence,
                "stripe_event_audit.append",
                "StripeEventAudit",
                auditWatch.ElapsedMilliseconds,
                true,
                item.Outcome);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Persistence failed. LogCategory={LogCategory} Outcome={Outcome} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.PersistenceFailed,
                "stripe_event_audit.append",
                "StripeEventAudit",
                auditWatch.ElapsedMilliseconds);
        }
    }

    private HttpResponseData BuildOutcomeResponse(
        HttpRequestData req,
        HttpStatusCode statusCode,
        string outcome,
        string reason,
        long durationMs)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            outcome,
            reason,
            (int)statusCode,
            durationMs);

        return req.CreateResponse(statusCode);
    }

    private static string NormalizeOutcomeForLogs(string? dispatchOutcome)
    {
        var normalized = LogContext.NormalizeOutcomeAlias(dispatchOutcome);
        return normalized switch
        {
            "skipped_duplicate" => LogContext.Outcomes.SkippedDuplicate,
            "skipped_out_of_order" => LogContext.Outcomes.SkippedOutOfOrder,
            "skipped_no_customer" => LogContext.Outcomes.SkippedNoCustomer,
            "skipped_unhandled" => LogContext.Outcomes.SkippedUnhandled,
            "failed_parse" => LogContext.Outcomes.ValidationFailed,
            "failed" => LogContext.Outcomes.DependencyFailed,
            "applied" => LogContext.Outcomes.Applied,
            _ => LogContext.Outcomes.Completed
        };
    }

    private async Task NotifyUnhandledPaymentFailureAsync(
        Stripe.Event stripeEvent,
        DateTimeOffset createdUtc,
        string reason,
        CancellationToken ct)
    {
        if (_adminPaymentAlerts is null)
            return;

        var obj = stripeEvent.RawJObject?["data"]?["object"];
        var lastPaymentError = obj?["last_payment_error"];
        var outcome = obj?["outcome"];

        await _adminPaymentAlerts.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_payment_failure_webhook",
            FailureStage = stripeEvent.Type ?? "stripe_payment_failure",
            FailureReason = reason,
            Severity = "Critical",
            OccurredAtUtc = createdUtc,
            StripeCustomerId = FirstRawString(obj, "customer"),
            StripeSubscriptionId =
                FirstRawString(obj, "subscription")
                ?? FirstRawString(obj?["parent"]?["subscription_details"], "subscription"),
            StripeInvoiceId = FirstRawString(obj, "invoice"),
            StripeEventId = stripeEvent.Id,
            StripeCheckoutSessionId = stripeEvent.Type == "checkout.session.async_payment_failed"
                ? FirstRawString(obj, "id")
                : null,
            Details =
            {
                ["stripeEventType"] = stripeEvent.Type,
                ["objectId"] = FirstRawString(obj, "id"),
                ["objectType"] = FirstRawString(obj, "object"),
                ["paymentIntentId"] = stripeEvent.Type == "payment_intent.payment_failed" ? FirstRawString(obj, "id") : FirstRawString(obj, "payment_intent"),
                ["chargeId"] = stripeEvent.Type == "charge.failed" ? FirstRawString(obj, "id") : FirstRawString(obj, "latest_charge"),
                ["status"] = FirstRawString(obj, "status"),
                ["amount"] = FirstRawString(obj, "amount"),
                ["currency"] = FirstRawString(obj, "currency"),
                ["failureCode"] = FirstRawString(obj, "failure_code") ?? FirstRawString(outcome, "reason"),
                ["failureMessage"] = FirstRawString(obj, "failure_message"),
                ["declineCode"] = FirstRawString(lastPaymentError, "decline_code"),
                ["lastPaymentErrorCode"] = FirstRawString(lastPaymentError, "code"),
                ["lastPaymentErrorMessage"] = FirstRawString(lastPaymentError, "message"),
                ["paymentMethod"] = FirstRawString(obj, "payment_method")
            }
        }, ct);
    }

    private static bool IsPaymentFailureEvent(string? eventType)
    {
        var normalized = eventType?.Trim().ToLowerInvariant();
        return normalized is "invoice.payment_failed"
            or "payment_intent.payment_failed"
            or "charge.failed"
            or "checkout.session.async_payment_failed"
            or "checkout.session.expired";
    }

    private static string? FirstRawString(JToken? token, string propertyName)
    {
        var value = token?[propertyName];
        if (value is null || value.Type == JTokenType.Null)
            return null;

        if (value.Type == JTokenType.Object)
            return value["id"]?.ToString();

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
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

    private sealed record DispatchResult(
        string Outcome,
        string? Reason,
        string? AccessMode,
        string? AccessSource);
}
