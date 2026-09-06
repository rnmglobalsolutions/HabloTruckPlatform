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
            "payment_intent.payment_failed" => ParsePaymentIntentFailed(stripeEvent),
            "charge.failed" => ParseChargeFailed(stripeEvent),
            "checkout.session.async_payment_failed" => ParseCheckoutAsyncPaymentFailed(stripeEvent),
            "checkout.session.expired" => ParseCheckoutExpired(stripeEvent),
            "customer.updated" => ParseCustomerUpdated(stripeEvent),
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
                CheckoutMode = session.Mode,
                Metadata = session.Metadata,
                PriceId = priceId,
                Interval = interval,
                Quantity = TryGetCheckoutQuantity(raw)
            }));
    }

    private static StripeParsedEvent ParseCheckoutAsyncPaymentFailed(Event e)
    {
        var session = e.Data.Object as Stripe.Checkout.Session
                      ?? throw new InvalidOperationException("Invalid checkout.session");

        var raw = session.RawJObject;

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                EventType = e.Type,
                CustomerId = FirstRawString(raw, "customer") ?? session.CustomerId,
                CustomerEmail = session.CustomerDetails?.Email
                                ?? FirstRawString(raw?["customer_details"], "email")
                                ?? FirstRawString(raw, "customer_email"),
                SubscriptionId = FirstRawString(raw, "subscription") ?? session.SubscriptionId,
                CheckoutSessionId = FirstRawString(raw, "id") ?? session.Id,
                CheckoutMode = session.Mode ?? FirstRawString(raw, "mode"),
                PaymentIntentId = FirstRawString(raw, "payment_intent"),
                Status = session.Status ?? FirstRawString(raw, "status"),
                Amount = FirstRawLong(raw, "amount_total") ?? FirstRawLong(raw, "amount_subtotal"),
                Currency = FirstRawString(raw, "currency"),
                FailureCode = "checkout_async_payment_failed",
                FailureMessage = "Checkout async payment failed after the hosted session was created.",
                Metadata = session.Metadata
            }));
    }

    private static StripeParsedEvent ParseCheckoutExpired(Event e)
    {
        var session = e.Data.Object as Stripe.Checkout.Session
                      ?? throw new InvalidOperationException("Invalid checkout.session");

        var raw = session.RawJObject;

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                EventType = e.Type,
                CustomerId = FirstRawString(raw, "customer") ?? session.CustomerId,
                CustomerEmail = session.CustomerDetails?.Email
                                ?? FirstRawString(raw?["customer_details"], "email")
                                ?? FirstRawString(raw, "customer_email"),
                SubscriptionId = FirstRawString(raw, "subscription") ?? session.SubscriptionId,
                CheckoutSessionId = FirstRawString(raw, "id") ?? session.Id,
                CheckoutMode = session.Mode ?? FirstRawString(raw, "mode"),
                PaymentIntentId = FirstRawString(raw, "payment_intent"),
                Status = session.Status ?? FirstRawString(raw, "status"),
                Amount = FirstRawLong(raw, "amount_total") ?? FirstRawLong(raw, "amount_subtotal"),
                Currency = FirstRawString(raw, "currency"),
                FailureCode = "checkout_session_expired",
                FailureMessage = "Checkout session expired before payment was successfully completed.",
                Metadata = session.Metadata
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
        var currentPeriodEndUtc = TryGetInvoiceCurrentPeriodEnd(raw);
        var quantity = TryGetInvoiceQuantity(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = subscriptionId,
                PriceId = priceId,
                Interval = interval,
                CurrentPeriodEndUtc = currentPeriodEndUtc,
                Quantity = quantity
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
        var quantity = TryGetInvoiceQuantity(raw);

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = invoice.CustomerId,
                SubscriptionId = subscriptionId,
                PriceId = priceId,
                Interval = interval,
                Quantity = quantity,
                // Status optional here; handler can treat invoice.payment_failed as past_due signal
                Status = "payment_failed"
            }));
    }

    private static StripeParsedEvent ParsePaymentIntentFailed(Event e)
    {
        var paymentIntent = e.Data.Object as PaymentIntent
                            ?? throw new InvalidOperationException("Invalid payment_intent");

        var raw = paymentIntent.RawJObject;
        var lastPaymentError = raw?["last_payment_error"];

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                EventType = e.Type,
                CustomerId = FirstRawString(raw, "customer") ?? paymentIntent.CustomerId,
                CustomerEmail = FirstRawString(raw, "receipt_email"),
                PaymentIntentId = FirstRawString(raw, "id") ?? paymentIntent.Id,
                ChargeId = FirstRawString(raw, "latest_charge") ?? FirstRawString(lastPaymentError, "charge"),
                PaymentMethod = FirstRawString(lastPaymentError?["payment_method"], "id")
                                ?? FirstRawString(raw, "payment_method"),
                PaymentMethodType = FirstRawString(lastPaymentError?["payment_method"], "type")
                                    ?? FirstRawString(raw?["payment_method_types"]?.First),
                Amount = FirstRawLong(raw, "amount"),
                Currency = FirstRawString(raw, "currency"),
                Status = paymentIntent.Status ?? FirstRawString(raw, "status"),
                FailureCode = FirstRawString(lastPaymentError, "code"),
                FailureMessage = FirstRawString(lastPaymentError, "message"),
                DeclineCode = FirstRawString(lastPaymentError, "decline_code"),
                Metadata = paymentIntent.Metadata
            }));
    }

    private static StripeParsedEvent ParseChargeFailed(Event e)
    {
        var charge = e.Data.Object as Charge
                     ?? throw new InvalidOperationException("Invalid charge");

        var raw = charge.RawJObject;
        var outcome = raw?["outcome"];
        var paymentMethodDetails = raw?["payment_method_details"];

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                EventType = e.Type,
                CustomerId = FirstRawString(raw, "customer") ?? charge.CustomerId,
                CustomerEmail = FirstRawString(raw?["billing_details"], "email"),
                PaymentIntentId = FirstRawString(raw, "payment_intent"),
                ChargeId = FirstRawString(raw, "id") ?? charge.Id,
                PaymentMethod = FirstRawString(raw, "payment_method"),
                PaymentMethodType = FirstRawString(paymentMethodDetails, "type"),
                Amount = FirstRawLong(raw, "amount"),
                Currency = FirstRawString(raw, "currency"),
                Status = charge.Status ?? FirstRawString(raw, "status"),
                FailureCode = FirstRawString(raw, "failure_code"),
                FailureMessage = FirstRawString(raw, "failure_message"),
                DeclineCode = FirstRawString(outcome, "reason"),
                Metadata = charge.Metadata
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
        var quantity = TryGetSubscriptionQuantity(raw);

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
                Quantity = quantity,
                Metadata = sub.Metadata
            }));
    }

    private static StripeParsedEvent ParseCustomerUpdated(Event e)
    {
        var customer = e.Data.Object as Customer
                       ?? throw new InvalidOperationException("Invalid customer");

        var raw = customer.RawJObject;
        var previousAttributes = e.RawJObject?["data"]?["previous_attributes"];

        return new StripeParsedEvent(
            e.Type,
            Stamp(e, new StripeEventData
            {
                CustomerId = customer.Id,
                PaymentMethodUpdated = HasUpdatedDefaultPaymentMethod(raw, previousAttributes)
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
        var quantity = TryGetSubscriptionQuantity(raw);

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
                Quantity = quantity,
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

    private static int? TryGetSubscriptionQuantity(JObject? raw)
    {
        var arr = raw?["items"]?["data"] as JArray;
        var item = arr?.FirstOrDefault() as JObject;
        return item?["quantity"]?.Value<int?>();
    }

    private static (string? priceId, string? interval) TryGetInvoicePrice(JObject? raw)
    {
        // invoice.items.data[0] or invoice.lines.data[0].price.id and recurring.interval
        var arr = raw?["items"]?["data"] as JArray
                  ?? raw?["lines"]?["data"] as JArray;
        var line = arr?.FirstOrDefault() as JObject;

        var priceId = line?["price"]?["id"]?.ToString();
        var interval = line?["price"]?["recurring"]?["interval"]?.ToString();

        return (NullIfBlank(priceId), NullIfBlank(interval));
    }
    private static DateTimeOffset? TryGetInvoiceCurrentPeriodEnd(JObject? raw)
    {
        var lines = raw?["items"]?["data"] as JArray
                    ?? raw?["lines"]?["data"] as JArray;

        if (lines is null || lines.Count == 0)
            return null;

        DateTimeOffset? max = null;

        foreach (var token in lines)
        {
            if (token is not JObject line)
                continue;

            var endToken = line["period"]?["end"]
                           ?? line["period_end"]
                           ?? line["current_period_end"];

            var candidate = ToDateTimeOffsetUtc(endToken);
            if (candidate is null)
                continue;

            if (max is null || candidate > max)
                max = candidate;
        }

        return max;
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

    private static int? TryGetCheckoutQuantity(JObject? raw)
    {
        var li = raw?["line_items"]?["data"]?.First;
        return li?["quantity"]?.Value<int?>();
    }

    private static int? TryGetInvoiceQuantity(JObject? raw)
    {
        var lines = raw?["lines"]?["data"] as JArray;
        var line = lines?.FirstOrDefault() as JObject;
        return line?["quantity"]?.Value<int?>();
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? FirstRawString(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
            return null;

        if (token.Type == JTokenType.Object)
            return FirstRawString(token, "id");

        var text = token.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? FirstRawString(JToken? token, string propertyName)
        => FirstRawString(token?[propertyName]);

    private static long? FirstRawLong(JToken? token, string propertyName)
    {
        var value = token?[propertyName];
        if (value is null || value.Type == JTokenType.Null)
            return null;

        return value.Value<long?>();
    }

    private static bool HasUpdatedDefaultPaymentMethod(JObject? current, JToken? previousAttributes)
    {
        var currentDefaultPaymentMethod = current?["invoice_settings"]?["default_payment_method"]?.ToString();
        if (string.IsNullOrWhiteSpace(currentDefaultPaymentMethod))
            return false;

        return previousAttributes?["invoice_settings"]?["default_payment_method"] is not null
               || previousAttributes?["invoice_settings"] is not null
               || previousAttributes?["default_payment_method"] is not null
               || previousAttributes?["default_source"] is not null
               || previousAttributes?["source"] is not null;
    }
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
    public string? EventType { get; set; }

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
    public string? CheckoutMode { get; set; }
    public string? CheckoutSessionId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? ChargeId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? DeclineCode { get; set; }
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PaymentMethodType { get; set; }
    public int? Quantity { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public bool? PaymentMethodUpdated { get; set; }
}
