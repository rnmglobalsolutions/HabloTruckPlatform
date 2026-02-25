using Stripe;

namespace HabloTruckPlatform.Application.Stripe;

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
        var session = e.Data.Object as Checkout.Session
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

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = invoice.SubscriptionId
            });
    }

    private static StripeParsedEvent ParseInvoiceFailed(Event e)
    {
        var invoice = e.Data.Object as Invoice
                      ?? throw new InvalidOperationException("Invalid invoice");

        return new StripeParsedEvent(
            e.Type,
            new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = invoice.SubscriptionId
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
    public string? CustomerId { get; init; }
    public string? SubscriptionId { get; init; }
    public string? Email { get; init; }
    public string? Status { get; init; }
    public IDictionary<string, string>? Metadata { get; init; }
}