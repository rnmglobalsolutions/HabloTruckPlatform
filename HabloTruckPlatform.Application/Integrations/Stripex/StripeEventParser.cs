using Newtonsoft.Json.Linq;
using Stripe;

namespace HabloTruckPlatform.Application.Integrations.Stripex;

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

        // Best-effort: try to read price id + interval from raw line_items (if expanded)
        var raw = session.RawJObject;
        var (priceId, interval) = TryGetCheckoutPrice(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = session.CustomerId,
                SubscriptionId = session.SubscriptionId,
                CustomerEmail = session.CustomerDetails?.Email,
                Metadata = session.Metadata,
                PriceId = priceId,
                Interval = interval,
                Quantity = TryGetCheckoutQuantity(raw)
            }));
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

        // Best-effort: price info from invoice lines if expanded
        var (priceId, interval) = TryGetInvoicePrice(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = subscriptionId,
                PriceId = priceId,
                Interval = interval
            }));
    }

    private static StripeParsedEvent ParseInvoiceFailed(Event e)
    {
        var invoice = e.Data.Object as Invoice
                      ?? throw new InvalidOperationException("Invalid invoice");

        var raw = invoice.RawJObject;

        var subscriptionId =
            raw?["subscription"]?.ToString()
            ?? raw?["parent"]?["subscription_details"]?["subscription"]?.ToString();

        var (priceId, interval) = TryGetInvoicePrice(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = subscriptionId,
                PriceId = priceId,
                Interval = interval,
                // Status optional here; handler can treat invoice.payment_failed as past_due signal
                Status = "payment_failed"
            }));
    }

    private static StripeParsedEvent ParseSubUpdated(Event e)
    {
        var sub = e.Data.Object as Subscription
                  ?? throw new InvalidOperationException("Invalid subscription");

        // Stripe timestamps are seconds since epoch in many fields. Stripe.net maps some to DateTime?
        // We'll use RawJObject for consistency across API versions.
        var raw = sub.RawJObject;

        var cancelAtPeriodEnd = raw?["cancel_at_period_end"]?.Value<bool?>();

        var currentPeriodEndUtc = EffectiveCurrentPeriodEndUtc(sub, raw);
        var canceledAtUtc = ToDateTimeOffsetUtc(raw?["canceled_at"]);
        var endedAtUtc = ToDateTimeOffsetUtc(raw?["ended_at"]);

        // Best-effort: pull the subscription's primary price id + interval
        var (priceId, interval) = TryGetSubscriptionPrice(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = sub.CustomerId,
                SubscriptionId = sub.Id,
                Status = sub.Status,
                CancelAtPeriodEnd = cancelAtPeriodEnd,
                CurrentPeriodEndUtc = currentPeriodEndUtc,
                CanceledAtUtc = canceledAtUtc,
                EndedAtUtc = endedAtUtc,
                PriceId = priceId,
                Interval = interval,
                Metadata = sub.Metadata
            }));
    }

    private static StripeParsedEvent ParseSubDeleted(Event e)
    {
        var sub = e.Data.Object as Subscription
                  ?? throw new InvalidOperationException("Invalid subscription");

        var raw = sub.RawJObject;

        var cancelAtPeriodEnd = raw?["cancel_at_period_end"]?.Value<bool?>();

        var currentPeriodEndUtc = EffectiveCurrentPeriodEndUtc(sub, raw);
        var canceledAtUtc = ToDateTimeOffsetUtc(raw?["canceled_at"]);
        var endedAtUtc = ToDateTimeOffsetUtc(raw?["ended_at"]);

        var (priceId, interval) = TryGetSubscriptionPrice(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = sub.CustomerId,
                SubscriptionId = sub.Id,
                Status = sub.Status, // often "canceled"
                CancelAtPeriodEnd = cancelAtPeriodEnd,
                CurrentPeriodEndUtc = currentPeriodEndUtc,
                CanceledAtUtc = canceledAtUtc,
                EndedAtUtc = endedAtUtc,
                PriceId = priceId,
                Interval = interval,
                Metadata = sub.Metadata
            }));
    }

    // ---------------------------
    // Helpers
    // ---------------------------

    private static StripeEventData Stamp(Event e, StripeEventData data)
    {
        data.StripeEventId = e.Id;
        data.StripeEventCreatedUtc = new DateTimeOffset(e.Created, TimeSpan.Zero);
        return data;
    }

    private static DateTimeOffset? EffectiveCurrentPeriodEndUtc(Subscription sub, JObject? raw)
    {
        DateTimeOffset? max = null;

        if (sub.Items?.Data is not null)
        {
            foreach (var item in sub.Items.Data)
            {
                var current = ToDateTimeOffsetUtc(item.CurrentPeriodEnd);
                if (current is null)
                    continue;

                if (max is null || current > max)
                    max = current;
            }
        }

        // Fallback for payload variants where item period end is not expanded.
        return max ?? ToDateTimeOffsetUtc(raw?["current_period_end"]);
    }

    private static DateTimeOffset? ToDateTimeOffsetUtc(DateTime? value)
    {
        if (value is null || value.Value == default)
            return null;

        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return new DateTimeOffset(dt).ToUniversalTime();
    }

    private static DateTimeOffset? ToDateTimeOffsetUtc(DateTime value)
        => value == default ? null : ToDateTimeOffsetUtc((DateTime?)value);

    private static DateTimeOffset? ToDateTimeOffsetUtc(JToken? token)
    {
        if (token is null) return null;

        // Unix seconds
        if ((token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            && long.TryParse(token.ToString().Split('.')[0], out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime();

        if (DateTimeOffset.TryParse(token.ToString(), out var dto))
            return dto.ToUniversalTime();

        return null;
    }

    private static (string? priceId, string? interval) TryGetSubscriptionPrice(JObject? raw)
    {
        // subscription.items.data[0].price.id and .recurring.interval

        var arr = raw?["items"]?["data"] as JArray;
        var item = arr?.FirstOrDefault() as JObject;

        var priceId = item?["price"]?["id"]?.ToString();
        var interval = item?["price"]?["recurring"]?["interval"]?.ToString();

        return (NullIfBlank(priceId), NullIfBlank(interval));
    }

    private static (string? priceId, string? interval) TryGetInvoicePrice(JObject? raw)
    {
        // invoice.lines.data[0].price.id and recurring.interval
        var arr = raw?["items"]?["data"] as JArray;
        var line = arr?.FirstOrDefault() as JObject;

        var priceId = line?["price"]?["id"]?.ToString();
        var interval = line?["price"]?["recurring"]?["interval"]?.ToString();

        return (NullIfBlank(priceId), NullIfBlank(interval));
    }

    private static (string? priceId, string? interval) TryGetCheckoutPrice(JObject? raw)
    {
        // checkout.session: if you expand line_items, it may be present:
        // session.line_items.data[0].price.id and recurring.interval
        var arr = raw?["line_items"]?["data"] as JArray;
        var li = arr?.FirstOrDefault() as JObject;

        var priceId = li?["price"]?["id"]?.ToString();
        var interval = li?["price"]?["recurring"]?["interval"]?.ToString();

        return (NullIfBlank(priceId), NullIfBlank(interval));
    }

    private static int TryGetCheckoutQuantity(JObject? raw)
    {
        var li = raw?["line_items"]?["data"]?.First;
        var q = li?["quantity"]?.Value<int?>();
        return q ?? 0;
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record StripeParsedEvent(string EventType, StripeEventData? Data);

public sealed class StripeEventData
{
    // Injected by parser
    public string StripeEventId { get; set; } = default!;
    public DateTimeOffset StripeEventCreatedUtc { get; set; }

    // Core identifiers
    public string? CustomerId { get; set; }
    public string? SubscriptionId { get; set; }

    // Status (subscription.updated/deleted etc)
    public string? Status { get; set; }

    // Plan facts
    public string? PriceId { get; set; }   // primary price id (best-effort)
    public string? Interval { get; set; }  // "month" | "year" (best-effort)

    // Cancellation/period facts (critical for upgrade/downgrade correctness)
    public bool? CancelAtPeriodEnd { get; set; }
    public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
    public DateTimeOffset? CanceledAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }

    // Checkout extras
    public string? CustomerEmail { get; set; }
    public int Quantity { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

