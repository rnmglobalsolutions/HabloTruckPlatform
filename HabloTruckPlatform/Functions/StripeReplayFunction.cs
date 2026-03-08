using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class StripeReplayFunction
{
    private readonly IStripeAdminClient _stripeAdminClient;
    private readonly IStripeSubscriptionHandler _handler;

    public StripeReplayFunction(
        IStripeAdminClient stripeAdminClient,
        IStripeSubscriptionHandler handler)
    {
        _stripeAdminClient = stripeAdminClient;
        _handler = handler;
    }

    [Function("StripeReplayEvent")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/stripe/replay")] HttpRequestData req,
        FunctionContext ctx)
    {
        var body = await JsonSerializer.DeserializeAsync<ReplayRequest>(req.Body, cancellationToken: ctx.CancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.EventId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Missing eventId.");
            return bad;
        }

        var data = await _stripeAdminClient.GetEventDataAsync(body.EventId, ctx.CancellationToken);
        if (data is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteStringAsync("Stripe event not found.");
            return notFound;
        }

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
                var badType = req.CreateResponse(HttpStatusCode.BadRequest);
                await badType.WriteStringAsync("Unsupported or missing eventType.");
                return badType;
        }

        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteStringAsync("Replay executed.");
        return ok;
    }

    private sealed record ReplayRequest(string EventId, string? EventType);
}