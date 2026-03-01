using Newtonsoft.Json.Linq;
using Stripe;

namespace HabloTruckPlatform.Application.Stripex;

public sealed class StripeEventParser
{
    public StripeParsedEvent Parse(Event stripeEvent)
    {
        var type = stripeEvent.Type;

        return type switch
        {
            "checkout.session.completed" => ParseCheckout(stripeEvent),
            "invoice.paid" => ParseInvoicePaid(stripeEvent),
            "invoice.payment_failed" => ParseInvoiceFailed(stripeEvent),
            "customer.subscription.updated" => ParseSubUpdated(stripeEvent),
            "customer.subscription.deleted" => ParseSubDeleted(stripeEvent),
            _ => new StripeParsedEvent(type, null)
        };
    }

    private static StripeParsedEvent ParseCheckout(Event e)
    {
        var session = e.Data.Object as Stripe.Checkout.Session
                      ?? throw new InvalidOperationException("Invalid checkout.session");

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = session.CustomerId,
                SubscriptionId = session.SubscriptionId,
                Email = session.CustomerDetails?.Email,
                Metadata = session.Metadata
            });
    }

    private static StripeParsedEvent ParseInvoicePaid(Event e)
    {
        var invoice = e.Data.Object as Invoice
                      ?? throw new InvalidOperationException("Invalid invoice");

        var raw = invoice.RawJObject;

        // Depending on API version, subscription can be here:
        // invoice.subscription OR invoice.parent.subscription_details.subscription
        var subscriptionId =
            raw?["subscription"]?.ToString()
            ?? raw?["parent"]?["subscription_details"]?["subscription"]?.ToString();

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = subscriptionId
            });
    }

    private static StripeParsedEvent ParseInvoiceFailed(Event e)
    {
        var invoice = e.Data.Object as Invoice
                      ?? throw new InvalidOperationException("Invalid invoice");

        var raw = invoice.RawJObject;

        // Chequear esto con el actual json object sent from Stripe
        var subscriptionId =
            raw?["subscription"]?.ToString()
            ?? raw?["parent"]?["subscription_details"]?["subscription"]?.ToString();

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = subscriptionId
            });
    }

    private static StripeParsedEvent ParseSubUpdated(Event e)
    {
        var sub = e.Data.Object as Subscription
                  ?? throw new InvalidOperationException("Invalid subscription");

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = sub.CustomerId,
                SubscriptionId = sub.Id,
                Status = sub.Status
            });
    }

    private static StripeParsedEvent ParseSubDeleted(Event e)
    {
        var sub = e.Data.Object as Subscription
                  ?? throw new InvalidOperationException("Invalid subscription");

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = sub.CustomerId,
                SubscriptionId = sub.Id,
                Status = sub.Status
            });
    }
}

public sealed record StripeParsedEvent(string EventType, StripeEventData? Data);

public sealed class StripeEventData
{
    // Injected by webhook (or parser)
    public string StripeEventId { get; set; } = default!;
    public DateTimeOffset StripeEventCreatedUtc { get; set; }

    // Core identifiers
    public string? CustomerId { get; set; }
    public string? SubscriptionId { get; set; }

    // Status (subscription.updated, invoice.payment_failed, etc.)
    public string? Status { get; set; }

    // Optional fields (checkout/session)
    public string? Email { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}