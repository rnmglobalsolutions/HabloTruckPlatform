using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Stripe;

namespace HabloTruckPlatform.Functions;

public sealed class StripeWebhookFunction
{
    private readonly IStripeEventStore _eventStore;
    private readonly StripeSubscriptionHandler _subHandler;
    private readonly CheckoutSessionHandler _checkoutHandler;

    private readonly string _webhookSecret;

    public StripeWebhookFunction(
        IStripeEventStore eventStore,
        StripeSubscriptionHandler subHandler,
        CheckoutSessionHandler checkoutHandler)
    {
        _eventStore = eventStore;
        _subHandler = subHandler;
        _checkoutHandler = checkoutHandler;

        _webhookSecret = Environment.GetEnvironmentVariable("Stripe__WebhookSecret")
                         ?? throw new InvalidOperationException("Missing Stripe__WebhookSecret setting.");
    }

    [Function("StripeWebhook")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/webhook")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync();

        if (!req.Headers.TryGetValues("Stripe-Signature", out var sigHeaders))
            return await Ok(req); // don't leak info

        var sigHeader = sigHeaders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sigHeader))
            return await Ok(req);

        Event stripeEvent;
        try
        {
            // Verify signature + parse event
            stripeEvent = EventUtility.ConstructEvent(body, sigHeader, _webhookSecret);
        }
        catch
        {
            // Invalid signature or bad payload
            return await Ok(req);
        }

        // Convert Stripe unix created -> DateTimeOffset
        var createdUtc = new DateTimeOffset(
            DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc));

        // Idempotency gate (Stripe retries are normal)
        var firstTime = await _eventStore.TryMarkProcessedAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            createdUtc,
            executionContext.CancellationToken);

        if (!firstTime)
            return await Ok(req);

        try
        {
            switch (stripeEvent.Type)
            {
                // -------------------
                // Individual subscription signals
                // -------------------
                case "customer.subscription.updated":
                    {
                        var sub = stripeEvent.Data.Object as Subscription;
                        if (sub is null) break;

                        var dto = new StripeSubscriptionUpdate(
                            StripeEventId: stripeEvent.Id,
                            StripeEventCreatedUtc: createdUtc,
                            StripeCustomerId: sub.CustomerId,
                            StripeSubscriptionId: sub.Id,
                            SubscriptionStatus: sub.Status);

                        await _subHandler.HandleSubscriptionUpdatedAsync(dto, executionContext.CancellationToken);
                        break;
                    }

                case "customer.subscription.deleted":
                    {
                        var sub = stripeEvent.Data.Object as Subscription;
                        if (sub is null) break;

                        var dto = new StripeSubscriptionDeleted(
                            StripeEventId: stripeEvent.Id,
                            StripeEventCreatedUtc: createdUtc,
                            StripeCustomerId: sub.CustomerId,
                            StripeSubscriptionId: sub.Id);

                        await _subHandler.HandleSubscriptionDeletedAsync(dto, executionContext.CancellationToken);
                        break;
                    }

                case "invoice.paid":
                    {
                        var inv = stripeEvent.Data.Object as Invoice;
                        if (inv is null) break;

                        var dto = new StripeInvoicePaid(
                            StripeEventId: stripeEvent.Id,
                            StripeEventCreatedUtc: createdUtc,
                            StripeCustomerId: inv.CustomerId);

                        await _subHandler.HandleInvoicePaidAsync(dto, executionContext.CancellationToken);
                        break;
                    }

                case "invoice.payment_failed":
                    {
                        var inv = stripeEvent.Data.Object as Invoice;
                        if (inv is null) break;

                        var dto = new StripeInvoicePaymentFailed(
                            StripeEventId: stripeEvent.Id,
                            StripeEventCreatedUtc: createdUtc,
                            StripeCustomerId: inv.CustomerId);

                        await _subHandler.HandleInvoicePaymentFailedAsync(dto, executionContext.CancellationToken);
                        break;
                    }

                // -------------------
                // B2B packs purchase
                // -------------------
                case "checkout.session.completed":
                    {
                        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                        if (session is null) break;

                        // Metadata keys you must set in Stripe Checkout:
                        // company_id, company_name, admin_email, seats_total, term
                        var md = session.Metadata ?? new Dictionary<string, string>();

                        var companyId = Get(md, "company_id") ?? "";
                        var companyName = Get(md, "company_name");
                        var adminEmail = Get(md, "admin_email")?.Trim().ToLowerInvariant();

                        var seatsTotal = TryInt(Get(md, "seats_total")) ?? 0;
                        var term = (Get(md, "term") ?? "annual").Trim().ToLowerInvariant();

                        var dto = new StripeCheckoutSessionCompleted(
                            StripeEventId: stripeEvent.Id,
                            StripeEventCreatedUtc: createdUtc,
                            StripeCustomerId: session.CustomerId,
                            CheckoutSessionId: session.Id,
                            PaymentIntentId: session.PaymentIntentId,
                            CompanyId: companyId,
                            CompanyName: companyName,
                            AdminEmailNormalized: adminEmail,
                            SeatsTotal: seatsTotal,
                            Term: term);

                        await _checkoutHandler.HandleCheckoutSessionCompletedAsync(dto, executionContext.CancellationToken);
                        break;
                    }

                default:
                    // ignore other events
                    break;
            }
        }
        catch
        {
            // IMPORTANT: For webhook resilience, return 200 so Stripe doesn't keep retrying forever.
            // You can add FailedAction outbox here later for "poison" events.
        }

        return await Ok(req);
    }

    private static async Task<HttpResponseData> Ok(HttpRequestData req)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteStringAsync("ok");
        return res;
    }

    private static string? Get(IDictionary<string, string> md, string key)
        => md.TryGetValue(key, out var v) ? v : null;

    private static int? TryInt(string? s)
        => int.TryParse(s, out var n) ? n : null;
}