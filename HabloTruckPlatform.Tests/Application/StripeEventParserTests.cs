using System.Reflection;
using HabloTruckPlatform.Application.Integrations.Stripex;
using Newtonsoft.Json.Linq;
using Stripe;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeEventParserTests
{
    private readonly StripeEventParser _sut = new();

    [Fact]
    public void Parse_SubscriptionUpdated_Should_UseLatestItemCurrentPeriodEnd_FromItemsData()
    {
        var itemEnd1 = Utc(2026, 4, 1);
        var itemEnd2 = Utc(2026, 4, 15);

        var sub = new Subscription();
        SetProperty(sub, "Id", "sub_items_latest");
        SetProperty(sub, "CustomerId", "cus_items_latest");
        SetProperty(sub, "Status", "active");

        var items = new StripeList<SubscriptionItem>();
        SetProperty(items, "Data", new List<SubscriptionItem>
        {
            NewSubscriptionItem(itemEnd1),
            NewSubscriptionItem(itemEnd2)
        });
        SetProperty(sub, "Items", items);

        SetProperty(sub, "RawJObject", JObject.Parse(@"{
  'cancel_at_period_end': true,
  'current_period_end': 1770000000,
  'items': {
    'data': [
      {
        'price': { 'id': 'price_monthly', 'recurring': { 'interval': 'month' } }
      }
    ]
  }
}"));

        var evt = NewEvent("customer.subscription.updated", sub, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.Equal("customer.subscription.updated", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_items_latest", parsed.Data!.CustomerId);
        Assert.Equal("sub_items_latest", parsed.Data.SubscriptionId);
        Assert.True(parsed.Data.CancelAtPeriodEnd);
        Assert.Equal(itemEnd2, parsed.Data.CurrentPeriodEndUtc);
        Assert.Equal("price_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
    }

    [Fact]
    public void Parse_SubscriptionUpdated_Should_FallbackToRawCurrentPeriodEnd_WhenItemsAreMissing()
    {
        var fallbackEnd = Utc(2026, 5, 1);
        var rawEpoch = fallbackEnd.ToUnixTimeSeconds();

        var sub = new Subscription();
        SetProperty(sub, "Id", "sub_raw_fallback");
        SetProperty(sub, "CustomerId", "cus_raw_fallback");
        SetProperty(sub, "Status", "active");
        SetProperty(sub, "Items", new StripeList<SubscriptionItem> { Data = new List<SubscriptionItem>() });
        SetProperty(sub, "RawJObject", JObject.Parse($@"{{
  'cancel_at_period_end': false,
  'current_period_end': {rawEpoch},
  'items': {{ 'data': [] }}
}}"));

        var evt = NewEvent("customer.subscription.updated", sub, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal(fallbackEnd, parsed.Data!.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Parse_SubscriptionDeleted_Should_HandleMissingOptionalFieldsSafely()
    {
        var sub = new Subscription();
        SetProperty(sub, "Id", "sub_deleted_missing");
        SetProperty(sub, "CustomerId", "cus_deleted_missing");
        SetProperty(sub, "Status", "canceled");
        SetProperty(sub, "Items", new StripeList<SubscriptionItem> { Data = new List<SubscriptionItem>() });
        SetProperty(sub, "RawJObject", JObject.Parse(@"{
  'cancel_at_period_end': false,
  'items': { 'data': [] }
}"));

        var evt = NewEvent("customer.subscription.deleted", sub, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal("sub_deleted_missing", parsed.Data!.SubscriptionId);
        Assert.Null(parsed.Data.CanceledAtUtc);
        Assert.Null(parsed.Data.EndedAtUtc);
        Assert.Null(parsed.Data.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Parse_InvoicePaid_Should_ParseSubscriptionAndPrice_FromInvoicePayload()
    {
        var invoice = new Invoice();
        SetProperty(invoice, "CustomerId", "cus_invoice_paid");
        SetProperty(invoice, "RawJObject", JObject.Parse(@"{
  'parent': { 'subscription_details': { 'subscription': 'sub_invoice_paid' } },
  'items': {
    'data': [
      {
        'price': { 'id': 'price_annual', 'recurring': { 'interval': 'year' } }
      }
    ]
  }
}"));

        var evt = NewEvent("invoice.paid", invoice, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_invoice_paid", parsed.Data!.CustomerId);
        Assert.Equal("sub_invoice_paid", parsed.Data.SubscriptionId);
        Assert.Equal("price_annual", parsed.Data.PriceId);
        Assert.Equal("year", parsed.Data.Interval);
    }

    [Fact]
    public void Parse_InvoicePaid_Should_ExtractCurrentPeriodEnd_FromInvoiceLinePeriodEnd()
    {
        var end1 = Utc(2026, 4, 1);
        var end2 = Utc(2026, 5, 1);

        var invoice = new Invoice();
        SetProperty(invoice, "CustomerId", "cus_invoice_period_end");
        SetProperty(invoice, "RawJObject", JObject.Parse($@"{{
  'subscription': 'sub_invoice_period_end',
  'items': {{
    'data': [
      {{
        'price': {{ 'id': 'price_monthly', 'recurring': {{ 'interval': 'month' }} }},
        'period': {{ 'end': {end1.ToUnixTimeSeconds()} }}
      }},
      {{
        'price': {{ 'id': 'price_monthly', 'recurring': {{ 'interval': 'month' }} }},
        'period': {{ 'end': {end2.ToUnixTimeSeconds()} }}
      }}
    ]
  }}
}}"));

        var evt = NewEvent("invoice.paid", invoice, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal("sub_invoice_period_end", parsed.Data!.SubscriptionId);
        Assert.Equal(end2, parsed.Data.CurrentPeriodEndUtc);
    }
    [Fact]
    public void Parse_InvoicePaymentFailed_Should_SetPaymentFailedStatus()
    {
        var invoice = new Invoice();
        SetProperty(invoice, "CustomerId", "cus_invoice_failed");
        SetProperty(invoice, "RawJObject", JObject.Parse(@"{
  'subscription': 'sub_invoice_failed',
  'items': {
    'data': [
      {
        'price': { 'id': 'price_monthly', 'recurring': { 'interval': 'month' } }
      }
    ]
  }
}"));

        var evt = NewEvent("invoice.payment_failed", invoice, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_invoice_failed", parsed.Data!.CustomerId);
        Assert.Equal("sub_invoice_failed", parsed.Data.SubscriptionId);
        Assert.Equal("payment_failed", parsed.Data.Status);
        Assert.Equal("price_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
    }


    [Fact]
    public void Parse_CheckoutCompleted_Should_ExtractCustomerSubscriptionAndExpandedLineItemFacts()
    {
        var session = new global::Stripe.Checkout.Session();
        SetProperty(session, "CustomerId", "cus_checkout_1");
        SetProperty(session, "SubscriptionId", "sub_checkout_1");
        SetProperty(session, "CustomerDetails", new global::Stripe.Checkout.SessionCustomerDetails
        {
            Email = "driver@hablotruck.com"
        });
        SetProperty(session, "Metadata", new Dictionary<string, string>
        {
            ["planType"] = "individual_monthly",
            ["manychatSubscriberId"] = "sid_checkout_1"
        });
        SetProperty(session, "RawJObject", JObject.Parse(@"{
  'line_items': {
    'data': [
      {
        'quantity': 2,
        'price': { 'id': 'price_checkout_monthly', 'recurring': { 'interval': 'month' } }
      }
    ]
  }
}"));

        var evt = NewEvent("checkout.session.completed", session, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_checkout_1", parsed.Data!.CustomerId);
        Assert.Equal("sub_checkout_1", parsed.Data.SubscriptionId);
        Assert.Equal("driver@hablotruck.com", parsed.Data.CustomerEmail);
        Assert.Equal("price_checkout_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal(2, parsed.Data.Quantity);
        Assert.NotNull(parsed.Data.Metadata);
        Assert.Equal("individual_monthly", parsed.Data.Metadata!["planType"]);
    }
    [Fact]
    public void Parse_InvoicePaid_Should_PreferTopLevelSubscription_WhenPresent()
    {
        var invoice = new Invoice();
        SetProperty(invoice, "CustomerId", "cus_invoice_direct");
        SetProperty(invoice, "RawJObject", JObject.Parse(@"{
  'subscription': 'sub_invoice_direct',
  'parent': { 'subscription_details': { 'subscription': 'sub_invoice_parent' } },
  'items': {
    'data': [
      {
        'price': { 'id': 'price_monthly', 'recurring': { 'interval': 'month' } }
      }
    ]
  }
}"));

        var evt = NewEvent("invoice.paid", invoice, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal("sub_invoice_direct", parsed.Data!.SubscriptionId);
        Assert.Equal("price_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
    }

    [Fact]
    public void Parse_SubscriptionUpdated_Should_ParseCanceledAndEndedTimestamps_FromRawPayload()
    {
        var canceledAt = Utc(2026, 3, 9);
        var endedAt = Utc(2026, 3, 10);

        var sub = new Subscription();
        SetProperty(sub, "Id", "sub_with_timestamps");
        SetProperty(sub, "CustomerId", "cus_with_timestamps");
        SetProperty(sub, "Status", "canceled");
        SetProperty(sub, "Items", new StripeList<SubscriptionItem> { Data = new List<SubscriptionItem>() });
        SetProperty(sub, "RawJObject", JObject.Parse($@"{{
  'cancel_at_period_end': true,
  'canceled_at': {canceledAt.ToUnixTimeSeconds()},
  'ended_at': {endedAt.ToUnixTimeSeconds()},
  'items': {{ 'data': [] }}
}}"));

        var evt = NewEvent("customer.subscription.updated", sub, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.NotNull(parsed.Data);
        Assert.Equal(canceledAt, parsed.Data!.CanceledAtUtc);
        Assert.Equal(endedAt, parsed.Data.EndedAtUtc);
    }

    [Fact]
    public void Parse_UnhandledEventType_Should_ReturnNullDataWithoutThrowing()
    {
        var payload = new Customer();
        SetProperty(payload, "Id", "cus_unhandled");

        var evt = NewEvent("customer.created", payload, Utc(2026, 3, 10));

        var parsed = _sut.Parse(evt);

        Assert.Equal("customer.created", parsed.EventType);
        Assert.Null(parsed.Data);
    }

    private static Event NewEvent(string type, object payload, DateTimeOffset createdUtc)
    {
        var stripeEvent = new Event();
        SetProperty(stripeEvent, "Id", $"evt_{Guid.NewGuid():N}");
        SetProperty(stripeEvent, "Type", type);
        SetProperty(stripeEvent, "Created", createdUtc.UtcDateTime);

        var data = new EventData();
        SetProperty(data, "Object", payload);
        SetProperty(stripeEvent, "Data", data);

        return stripeEvent;
    }

    private static SubscriptionItem NewSubscriptionItem(DateTimeOffset periodEndUtc)
    {
        var item = new SubscriptionItem();
        SetProperty(item, "CurrentPeriodEnd", periodEndUtc.UtcDateTime);
        return item;
    }

    private static DateTimeOffset Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var prop = target.GetType().GetProperty(propertyName, flags);
        if (prop is not null)
        {
            if (prop.SetMethod is not null)
            {
                prop.SetValue(target, value);
                return;
            }

            var backing = target.GetType().GetField($"<{propertyName}>k__BackingField", flags);
            if (backing is not null)
            {
                backing.SetValue(target, value);
                return;
            }
        }

        throw new InvalidOperationException($"Property '{propertyName}' not found on {target.GetType().FullName}.");
    }
}




