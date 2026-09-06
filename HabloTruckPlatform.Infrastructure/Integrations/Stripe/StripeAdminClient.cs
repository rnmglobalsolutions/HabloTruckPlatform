using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeAdminClient : IStripeAdminClient
{
    private readonly IStripeSubscriptionGateway _subscriptions;
    private readonly StripeOptions _options;
    private readonly ILogger<StripeAdminClient> _logger;

    public StripeAdminClient(
        IStripeSubscriptionGateway subscriptions,
        StripeOptions options,
        ILogger<StripeAdminClient>? logger = null)
    {
        _subscriptions = subscriptions;
        _options = options;
        _logger = logger ?? NullLogger<StripeAdminClient>.Instance;
    }

    public async Task<StripeEventData?> GetEventDataAsync(
        string eventId, CancellationToken ct = default)
    {
        var eventService = new EventService();
        var watch = Stopwatch.StartNew();

        var stripeEvent = await eventService.GetAsync(eventId, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} StripeEventId={StripeEventId}",
            "dependency",
            "stripe",
            "get_event",
            "Stripe API",
            watch.ElapsedMilliseconds,
            true,
            stripeEvent is not null,
            eventId);

        if (stripeEvent is null)
            return null;

        var parser = new StripeEventParser();
        var parsed = parser.Parse(stripeEvent);

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} EventType={EventType}",
            "step",
            "parse_event",
            parsed.Data is null ? "no_action_needed" : "applied",
            parsed.EventType);

        return parsed.Data;
    }

    public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default)
        => _subscriptions.GetSubscriptionAsync(subscriptionId, ct);

    public async Task<StripePaymentMethodUpdateSession> CreatePaymentMethodUpdateSessionAsync(
        string customerId,
        string? subscriptionId,
        string returnUrl,
        CancellationToken ct = default)
    {
        var normalizedCustomerId = customerId.Trim();
        var normalizedReturnUrl = returnUrl.Trim();
        var service = new global::Stripe.BillingPortal.SessionService();
        var watch = Stopwatch.StartNew();

        var options = new global::Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = normalizedCustomerId,
            ReturnUrl = normalizedReturnUrl,
            FlowData = new global::Stripe.BillingPortal.SessionFlowDataOptions
            {
                Type = "payment_method_update",
                AfterCompletion = new global::Stripe.BillingPortal.SessionFlowDataAfterCompletionOptions
                {
                    Type = "redirect",
                    Redirect = new global::Stripe.BillingPortal.SessionFlowDataAfterCompletionRedirectOptions
                    {
                        ReturnUrl = normalizedReturnUrl
                    }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(_options.CustomerPortalConfigurationId))
            options.Configuration = _options.CustomerPortalConfigurationId.Trim();

        var session = await service.CreateAsync(options, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} SessionId={SessionId} CustomerId={CustomerId}",
            "dependency",
            "stripe",
            "billing_portal_payment_method_update_session_create",
            "Stripe API",
            watch.ElapsedMilliseconds,
            session is not null,
            session?.Id,
            normalizedCustomerId);

        return new StripePaymentMethodUpdateSession(
            SessionId: session?.Id ?? string.Empty,
            CustomerId: normalizedCustomerId,
            SubscriptionId: string.IsNullOrWhiteSpace(subscriptionId) ? null : subscriptionId.Trim(),
            Url: session?.Url ?? string.Empty);
    }

    public async Task<StripeOpenInvoiceRetryAttempt> RetryOpenInvoiceAsync(
        string customerId,
        string subscriptionId,
        CancellationToken ct = default)
    {
        var normalizedCustomerId = customerId.Trim();
        var normalizedSubscriptionId = subscriptionId.Trim();
        var invoiceService = new InvoiceService();
        var listWatch = Stopwatch.StartNew();

        var invoices = await invoiceService.ListAsync(new InvoiceListOptions
        {
            Customer = normalizedCustomerId,
            Subscription = normalizedSubscriptionId,
            Status = "open",
            Limit = 10
        }, cancellationToken: ct);

        var openInvoice = invoices.Data
            .OrderByDescending(static x => x.Created)
            .FirstOrDefault();

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "list_open_invoices",
            "Stripe API",
            listWatch.ElapsedMilliseconds,
            true,
            openInvoice is not null,
            normalizedSubscriptionId);

        if (openInvoice is null)
        {
            return new StripeOpenInvoiceRetryAttempt(
                CustomerId: normalizedCustomerId,
                SubscriptionId: normalizedSubscriptionId,
                InvoiceId: null,
                InvoiceStatus: null,
                CollectionMethod: null,
                InvoiceFound: false,
                PaymentAttempted: false,
                InvoicePaid: false);
        }

        if (!string.Equals(openInvoice.CollectionMethod, "charge_automatically", StringComparison.OrdinalIgnoreCase))
        {
            return new StripeOpenInvoiceRetryAttempt(
                CustomerId: normalizedCustomerId,
                SubscriptionId: normalizedSubscriptionId,
                InvoiceId: openInvoice.Id,
                InvoiceStatus: openInvoice.Status,
                CollectionMethod: openInvoice.CollectionMethod,
                InvoiceFound: true,
                PaymentAttempted: false,
                InvoicePaid: string.Equals(openInvoice.Status, "paid", StringComparison.OrdinalIgnoreCase));
        }

        var payWatch = Stopwatch.StartNew();
        var paidInvoice = await invoiceService.PayAsync(
            openInvoice.Id,
            new InvoicePayOptions
            {
                OffSession = true
            },
            new RequestOptions
            {
                IdempotencyKey = $"ht-retry-open-invoice:{openInvoice.Id}"
            },
            ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} InvoiceId={InvoiceId} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "pay_open_invoice",
            "Stripe API",
            payWatch.ElapsedMilliseconds,
            paidInvoice is not null,
            openInvoice.Id,
            normalizedSubscriptionId);

        return new StripeOpenInvoiceRetryAttempt(
            CustomerId: normalizedCustomerId,
            SubscriptionId: normalizedSubscriptionId,
            InvoiceId: paidInvoice?.Id ?? openInvoice.Id,
            InvoiceStatus: paidInvoice?.Status ?? openInvoice.Status,
            CollectionMethod: paidInvoice?.CollectionMethod ?? openInvoice.CollectionMethod,
            InvoiceFound: true,
            PaymentAttempted: true,
            InvoicePaid: string.Equals(paidInvoice?.Status, "paid", StringComparison.OrdinalIgnoreCase));
    }
}
