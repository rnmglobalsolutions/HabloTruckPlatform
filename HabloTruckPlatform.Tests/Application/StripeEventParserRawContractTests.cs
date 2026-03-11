using System.Globalization;
using HabloTruckPlatform.Application.Integrations.Stripex;
using Stripe;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeEventParserRawContractTests
{
    private const string WebhookSecret = "whsec_parser_contract";
    private const long SignatureTimestamp = 1770000000;

    private readonly StripeEventParser _sut = new();

    [Fact]
    public void Parse_CheckoutCompleted_RawFixture_ShouldExtractExpectedFields()
    {
        var parsed = _sut.Parse(ParseSignedEvent(BuildCheckoutCompletedEventJson(
            "evt_contract_checkout",
            "cus_contract_checkout",
            "sub_contract_checkout",
            "driver-contract@hablotruck.com")));

        Assert.Equal("checkout.session.completed", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_checkout", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_checkout", parsed.Data.SubscriptionId);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal("driver-contract@hablotruck.com", parsed.Data.CustomerEmail);
        Assert.Equal(2, parsed.Data.Quantity);
    }

    [Fact]
    public void Parse_InvoicePaid_RawFixture_ShouldExtractExpectedFieldsIncludingCurrentPeriodEnd()
    {
        var expectedPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(1772600000).ToUniversalTime();

        var parsed = _sut.Parse(ParseSignedEvent(BuildInvoicePaidEventJson(
            "evt_contract_invoice_paid",
            "cus_contract_paid",
            "sub_contract_paid")));

        Assert.Equal("invoice.paid", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_paid", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_paid", parsed.Data.SubscriptionId);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal(expectedPeriodEnd, parsed.Data.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Parse_InvoicePaid_WithParentSubscriptionDetails_ShouldExtractSubscriptionId()
    {
        var parsed = _sut.Parse(ParseSignedEvent(BuildInvoicePaidWithParentSubscriptionDetailsEventJson(
            "evt_contract_invoice_parent",
            "cus_contract_parent",
            "sub_contract_parent")));

        Assert.Equal("invoice.paid", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_parent", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_parent", parsed.Data.SubscriptionId);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
    }

    [Fact]
    public void Parse_InvoicePaid_WithLinesArray_ShouldExtractMaxPeriodEnd()
    {
        var expectedPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(1772900000).ToUniversalTime();

        var parsed = _sut.Parse(ParseSignedEvent(BuildInvoicePaidWithLinesEventJson(
            "evt_contract_invoice_lines",
            "cus_contract_lines",
            "sub_contract_lines")));

        Assert.Equal("invoice.paid", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_lines", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_lines", parsed.Data.SubscriptionId);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal(expectedPeriodEnd, parsed.Data.CurrentPeriodEndUtc);
    }

    [Fact]
    public void Parse_InvoicePaymentFailed_RawFixture_ShouldExtractExpectedFields()
    {
        var parsed = _sut.Parse(ParseSignedEvent(BuildInvoicePaymentFailedEventJson(
            "evt_contract_invoice_failed",
            "cus_contract_failed",
            "sub_contract_failed")));

        Assert.Equal("invoice.payment_failed", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_failed", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_failed", parsed.Data.SubscriptionId);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal("payment_failed", parsed.Data.Status);
    }

    [Fact]
    public void Parse_SubscriptionUpdated_RawFixture_ShouldExtractStatusCancelAndPeriodFacts()
    {
        var canceledAt = DateTimeOffset.FromUnixTimeSeconds(1769900000).ToUniversalTime();
        var expectedPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(1772600000).ToUniversalTime();

        var parsed = _sut.Parse(ParseSignedEvent(BuildSubscriptionUpdatedEventJson(
            "evt_contract_sub_updated",
            "cus_contract_updated",
            "sub_contract_updated")));

        Assert.Equal("customer.subscription.updated", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_updated", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_updated", parsed.Data.SubscriptionId);
        Assert.Equal("active", parsed.Data.Status);
        Assert.True(parsed.Data.CancelAtPeriodEnd);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal(expectedPeriodEnd, parsed.Data.CurrentPeriodEndUtc);
        Assert.Equal(canceledAt, parsed.Data.CanceledAtUtc);
        Assert.Null(parsed.Data.EndedAtUtc);
    }

    [Fact]
    public void Parse_SubscriptionDeleted_RawFixture_ShouldExtractEndedFacts()
    {
        var canceledAt = DateTimeOffset.FromUnixTimeSeconds(1772500000).ToUniversalTime();
        var endedAt = DateTimeOffset.FromUnixTimeSeconds(1772600000).ToUniversalTime();

        var parsed = _sut.Parse(ParseSignedEvent(BuildSubscriptionDeletedEventJson(
            "evt_contract_sub_deleted",
            "cus_contract_deleted",
            "sub_contract_deleted")));

        Assert.Equal("customer.subscription.deleted", parsed.EventType);
        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_contract_deleted", parsed.Data!.CustomerId);
        Assert.Equal("sub_contract_deleted", parsed.Data.SubscriptionId);
        Assert.Equal("canceled", parsed.Data.Status);
        Assert.True(parsed.Data.CancelAtPeriodEnd);
        Assert.Equal("price_ind_monthly", parsed.Data.PriceId);
        Assert.Equal("month", parsed.Data.Interval);
        Assert.Equal(canceledAt, parsed.Data.CanceledAtUtc);
        Assert.Equal(endedAt, parsed.Data.EndedAtUtc);
    }

    [Fact]
    public void Parse_IncompleteInvoicePayloads_ShouldFailSafelyAndPreserveExpectedNulls()
    {
        var missingCustomer = _sut.Parse(ParseSignedEvent(BuildInvoicePaidWithoutCustomerEventJson("evt_missing_customer", "sub_missing_customer")));
        Assert.NotNull(missingCustomer.Data);
        Assert.Null(missingCustomer.Data!.CustomerId);
        Assert.Equal("sub_missing_customer", missingCustomer.Data.SubscriptionId);

        var missingSubscription = _sut.Parse(ParseSignedEvent(BuildInvoicePaidWithoutSubscriptionEventJson("evt_missing_subscription", "cus_missing_subscription")));
        Assert.NotNull(missingSubscription.Data);
        Assert.Equal("cus_missing_subscription", missingSubscription.Data!.CustomerId);
        Assert.Null(missingSubscription.Data.SubscriptionId);
    }

    [Fact]
    public void Parse_SubscriptionUpdated_WithMissingItems_ShouldFallbackToRawCurrentPeriodEndSafely()
    {
        var expectedPeriodEnd = DateTimeOffset.FromUnixTimeSeconds(1772600000).ToUniversalTime();

        var parsed = _sut.Parse(ParseSignedEvent(BuildSubscriptionUpdatedWithoutItemsEventJson(
            "evt_missing_items",
            "cus_missing_items",
            "sub_missing_items")));

        Assert.NotNull(parsed.Data);
        Assert.Equal("cus_missing_items", parsed.Data!.CustomerId);
        Assert.Equal("sub_missing_items", parsed.Data.SubscriptionId);
        Assert.Equal("active", parsed.Data.Status);
        Assert.Equal(expectedPeriodEnd, parsed.Data.CurrentPeriodEndUtc);
        Assert.Null(parsed.Data.PriceId);
        Assert.Null(parsed.Data.Interval);
    }

    [Fact]
    public void Parse_UnknownEventType_RawFixture_ShouldReturnNullData()
    {
        var parsed = _sut.Parse(ParseSignedEvent(BuildUnknownEventJson("evt_contract_unknown")));

        Assert.Equal("customer.created", parsed.EventType);
        Assert.Null(parsed.Data);
    }

    [Fact]
    public void Parse_MalformedPayload_ShouldFailSafelyAtParserBoundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _sut.Parse(ParseSignedEvent(BuildMalformedInvoiceEventJson("evt_contract_malformed"))));
    }
    private static Event ParseSignedEvent(string payload)
    {
        var signature = CreateSignatureHeader(payload, WebhookSecret, SignatureTimestamp);

        return EventUtility.ConstructEvent(
            payload,
            signature,
            WebhookSecret,
            tolerance: 300,
            utcNow: SignatureTimestamp + 1,
            throwOnApiVersionMismatch: false);
    }

    private static string CreateSignatureHeader(string payload, string secret, long timestamp)
    {
        var ts = timestamp.ToString(CultureInfo.InvariantCulture);
        var sig = EventUtility.ComputeSignature(secret, ts, payload);
        return $"t={ts},v1={sig}";
    }

    private static string BuildSubscriptionUpdatedEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "customer.subscription.updated",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_123",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{subscriptionId}}",
      "object": "subscription",
      "customer": "{{customerId}}",
      "status": "active",
      "cancel_at_period_end": true,
      "canceled_at": 1769900000,
      "items": {
        "data": [
          {
            "current_period_end": 1772000000,
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            }
          },
          {
            "current_period_end": 1772600000,
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildSubscriptionDeletedEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "customer.subscription.deleted",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_999",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{subscriptionId}}",
      "object": "subscription",
      "customer": "{{customerId}}",
      "status": "canceled",
      "cancel_at_period_end": true,
      "canceled_at": 1772500000,
      "ended_at": 1772600000,
      "items": {
        "data": [
          {
            "current_period_end": 1772600000,
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildInvoicePaidEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.paid",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_456",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "in_test_1",
      "object": "invoice",
      "customer": "{{customerId}}",
      "subscription": "{{subscriptionId}}",
      "items": {
        "data": [
          {
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            },
            "period": {
              "end": 1772000000
            }
          },
          {
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            },
            "period": {
              "end": 1772600000
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildInvoicePaidWithParentSubscriptionDetailsEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.paid",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_parent_subscription",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "in_test_parent",
      "object": "invoice",
      "customer": "{{customerId}}",
      "parent": {
        "subscription_details": {
          "subscription": "{{subscriptionId}}"
        }
      },
      "items": {
        "data": [
          {
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            },
            "period": {
              "end": 1772800000
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildInvoicePaidWithLinesEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.paid",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_lines",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "in_test_lines",
      "object": "invoice",
      "customer": "{{customerId}}",
      "subscription": "{{subscriptionId}}",
      "lines": {
        "data": [
          {
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            },
            "period": {
              "end": 1772500000
            }
          },
          {
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            },
            "period": {
              "end": 1772900000
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildInvoicePaymentFailedEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.payment_failed",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_789",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "in_test_2",
      "object": "invoice",
      "customer": "{{customerId}}",
      "subscription": "{{subscriptionId}}",
      "items": {
        "data": [
          {
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildCheckoutCompletedEventJson(string eventId, string customerId, string subscriptionId, string email)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "checkout.session.completed",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_checkout",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "cs_test_123",
      "object": "checkout.session",
      "customer": "{{customerId}}",
      "subscription": "{{subscriptionId}}",
      "customer_details": {
        "email": "{{email}}"
      },
      "metadata": {
        "planType": "individual_monthly",
        "manychatSubscriberId": "sid_checkout"
      },
      "line_items": {
        "data": [
          {
            "quantity": 2,
            "price": {
              "id": "price_ind_monthly",
              "recurring": {
                "interval": "month"
              }
            }
          }
        ]
      }
    }
  }
}
""";

    private static string BuildInvoicePaidWithoutCustomerEventJson(string eventId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.paid",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_no_customer",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "in_no_customer",
      "object": "invoice",
      "customer": null,
      "subscription": "{{subscriptionId}}"
    }
  }
}
""";

    private static string BuildInvoicePaidWithoutSubscriptionEventJson(string eventId, string customerId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.paid",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_no_subscription",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "in_no_subscription",
      "object": "invoice",
      "customer": "{{customerId}}"
    }
  }
}
""";

    private static string BuildSubscriptionUpdatedWithoutItemsEventJson(string eventId, string customerId, string subscriptionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "customer.subscription.updated",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_missing_items",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{subscriptionId}}",
      "object": "subscription",
      "customer": "{{customerId}}",
      "status": "active",
      "cancel_at_period_end": false,
      "current_period_end": 1772600000
    }
  }
}
""";

    private static string BuildUnknownEventJson(string eventId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "customer.created",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_unknown",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "cus_unknown",
      "object": "customer",
      "email": "unknown@hablotruck.com"
    }
  }
}
""";

    private static string BuildMalformedInvoiceEventJson(string eventId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "invoice.paid",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_malformed",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "cus_not_invoice",
      "object": "customer",
      "email": "oops@hablotruck.com"
    }
  }
}
""";
}
