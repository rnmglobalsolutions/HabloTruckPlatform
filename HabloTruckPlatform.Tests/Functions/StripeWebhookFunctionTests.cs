using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Functions.Functions;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;

namespace HabloTruckPlatform.Domain.Tests.Functions;

public sealed class StripeWebhookFunctionTests
{
    [Fact]
    public void SignatureHelper_Should_BeAcceptedByStripeEventUtility()
    {
        const string secret = "whsec_test_webhook";
        var json = BuildSubscriptionUpdatedEventJson("evt_sig_check", "cus_sig_check", "sub_sig_check");
        const long timestamp = 1770000000;
        var header = CreateStripeSignatureHeader(json, secret, timestamp: timestamp);

        var parsed = EventUtility.ConstructEvent(
            json,
            header,
            secret,
            tolerance: 300,
            utcNow: timestamp + 1,
            throwOnApiVersionMismatch: false);

        Assert.NotNull(parsed);
        Assert.Equal("evt_sig_check", parsed.Id);
    }

    [Fact]
    public async Task Run_Should_ReturnBadRequest_When_SignatureIsInvalid()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildSubscriptionUpdatedEventJson("evt_bad_sig", "cus_bad_sig", "sub_bad_sig");
        var req = NewRequest(json, stripeSignatureHeader: "invalid");

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);
        Assert.Empty(fixture.AuditStore.Items);
    }

    [Fact]
    public async Task Run_Should_ReturnOkAndAuditFailedParse_WhenPayloadShapeIsInvalidForEventType()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildMalformedInvoiceEventJson("evt_malformed_payload");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("failed_parse", audit.Outcome);
        Assert.Equal("invoice.paid", audit.EventType);
        Assert.NotNull(audit.Error);
    }

    [Fact]
    public async Task Run_Should_ReturnBadRequest_When_JsonBodyIsMalformedEvenWithSignatureHeader()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        const string json = "{ not_valid_json }";
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);
        Assert.Empty(fixture.AuditStore.Items);
    }

    [Fact]
    public async Task Run_Should_IgnoreDuplicate_When_EventAlreadyProcessed()
    {
        var fixture = BuildFixture(new StubStripeEventStore(StripeEventProcessingStartResult.AlreadyProcessed));
        var json = BuildSubscriptionUpdatedEventJson("evt_dup", "cus_dup", "sub_dup");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("skipped_duplicate", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_ProcessFirstThenIgnoreDuplicate_ForSameInvoicePaidEvent()
    {
        var eventStore = new DeduplicatingStripeEventStore();
        var fixture = BuildFixture(eventStore);
        var json = BuildInvoicePaidEventJson("evt_dup_flow", "cus_dup_flow", "sub_dup_flow");

        var req1 = NewSignedRequest(json, fixture.WebhookSecret);
        var req2 = NewSignedRequest(json, fixture.WebhookSecret);

        var response1 = await fixture.Function.Run(req1, req1.FunctionContext);
        var response2 = await fixture.Function.Run(req2, req2.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        Assert.Equal(2, eventStore.StartResults.Count);
        Assert.Equal(StripeEventProcessingStartResult.Started, eventStore.StartResults[0]);
        Assert.Equal(StripeEventProcessingStartResult.AlreadyProcessed, eventStore.StartResults[1]);

        Assert.Single(fixture.Handler.Calls, x => x == "invoice_paid");

        var audits = fixture.AuditStore.Items.Where(x => x.StripeEventId == "evt_dup_flow").ToList();
        Assert.Equal(2, audits.Count);
        Assert.Single(audits, x => x.Outcome == "applied");
        Assert.Single(audits, x => x.Outcome == "skipped_duplicate");
    }
    [Fact]
    public async Task Run_Should_RecordSkippedOutOfOrderOutcome_When_HandlerReturnsOutOfOrderReason()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        fixture.Handler.SubscriptionUpdatedDecision = new AccessDecision(
            AccessMode.Full,
            AccessSource.Individual,
            null,
            "Ignored out-of-order Stripe event");

        var json = BuildSubscriptionUpdatedEventJson("evt_ooo", "cus_ooo", "sub_ooo");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sub_updated", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("skipped_out_of_order", audit.Outcome);
    }
    [Fact]
    public async Task Run_Should_RecordAppliedOutcomeAndAccessFields_When_HandlerReturnsDecision()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        fixture.Handler.SubscriptionUpdatedDecision = new AccessDecision(
            AccessMode.Full,
            AccessSource.Individual,
            null,
            "subscription refreshed");

        var json = BuildSubscriptionUpdatedEventJson("evt_updated_applied", "cus_updated_applied", "sub_updated_applied");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sub_updated", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("applied", audit.Outcome);
        Assert.Equal("subscription refreshed", audit.Reason);
        Assert.Equal("Full", audit.AccessMode);
        Assert.Equal("Individual", audit.AccessSource);
    }

    [Fact]
    public async Task Run_Should_DispatchInvoicePaid_ToCorrectHandler()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildInvoicePaidEventJson("evt_paid", "cus_paid", "sub_paid");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("invoice_paid", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("applied", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_DispatchInvoicePaymentFailed_ToCorrectHandler()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildInvoicePaymentFailedEventJson("evt_failed", "cus_failed", "sub_failed");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("invoice_failed", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("applied", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_SendAdminAlert_ForPaymentIntentPaymentFailed()
    {
        var alerts = new RecordingAdminPaymentAlertNotifier(AdminPaymentAlertDeliveryResult.Sent());
        var fixture = BuildFixture(new StubStripeEventStore(StripeEventProcessingStartResult.Started), alerts);
        var json = BuildPaymentIntentFailedEventJson(
            "evt_pi_failed",
            "cus_pi_failed",
            "pi_failed",
            "ch_failed",
            "insufficient_funds");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);

        var alert = Assert.Single(alerts.Alerts);
        Assert.Equal("stripe_payment_failure_webhook", alert.OperationName);
        Assert.Equal("payment_intent.payment_failed", alert.FailureStage);
        Assert.Equal("cus_pi_failed", alert.StripeCustomerId);
        Assert.Equal("evt_pi_failed", alert.StripeEventId);
        Assert.Equal("pi_failed", alert.Details["paymentIntentId"]);
        Assert.Equal("ch_failed", alert.Details["chargeId"]);
        Assert.Equal("insufficient_funds", alert.Details["declineCode"]);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("stripe_payment_failure_alert_sent", audit.Outcome);
        Assert.Equal("payment_intent.payment_failed:sendgrid_accepted", audit.Reason);
    }

    [Fact]
    public async Task Run_Should_SendAdminAlert_ForChargeFailed()
    {
        var alerts = new RecordingAdminPaymentAlertNotifier(AdminPaymentAlertDeliveryResult.Sent());
        var fixture = BuildFixture(new StubStripeEventStore(StripeEventProcessingStartResult.Started), alerts);
        var json = BuildChargeFailedEventJson("evt_charge_failed", "cus_charge_failed", "pi_charge_failed", "ch_charge_failed");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var alert = Assert.Single(alerts.Alerts);
        Assert.Equal("charge.failed", alert.FailureStage);
        Assert.Equal("ch_charge_failed", alert.Details["chargeId"]);
        Assert.Equal("pi_charge_failed", alert.Details["paymentIntentId"]);
        Assert.Equal("generic_decline", alert.Details["failureCode"]);
        Assert.Equal("generic_decline", alert.Details["declineCode"]);
    }

    [Fact]
    public async Task Run_Should_SendAdminAlert_ForCheckoutExpiredWithoutCustomer()
    {
        var alerts = new RecordingAdminPaymentAlertNotifier(AdminPaymentAlertDeliveryResult.Sent());
        var fixture = BuildFixture(new StubStripeEventStore(StripeEventProcessingStartResult.Started), alerts);
        var json = BuildCheckoutExpiredEventJson("evt_checkout_expired", "cs_expired_123");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);

        var alert = Assert.Single(alerts.Alerts);
        Assert.Equal("checkout.session.expired", alert.FailureStage);
        Assert.Equal("cs_expired_123", alert.StripeCheckoutSessionId);
        Assert.Equal("checkout_session_expired", alert.Details["failureCode"]);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("stripe_payment_failure_alert_sent", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_AuditSkipped_WhenPaymentFailureAlertIsDisabled()
    {
        var alerts = new RecordingAdminPaymentAlertNotifier(
            AdminPaymentAlertDeliveryResult.Skipped("admin_payment_alert_email_disabled"));
        var fixture = BuildFixture(new StubStripeEventStore(StripeEventProcessingStartResult.Started), alerts);
        var json = BuildPaymentIntentFailedEventJson(
            "evt_pi_skipped",
            "cus_pi_skipped",
            "pi_skipped",
            "ch_skipped",
            "generic_decline");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("stripe_payment_failure_alert_skipped", audit.Outcome);
        Assert.Equal("payment_intent.payment_failed:admin_payment_alert_email_disabled", audit.Reason);
    }

    [Fact]
    public async Task Run_Should_DispatchCustomerUpdated_ToCorrectHandler()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildCustomerUpdatedEventJson("evt_customer_updated", "cus_customer_updated");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("customer_updated", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("applied", audit.Outcome);
        Assert.Equal("customer.updated", audit.EventType);
    }

    [Fact]
    public async Task Run_Should_DispatchSubscriptionDeleted_ToCorrectHandler()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildSubscriptionDeletedEventJson("evt_deleted", "cus_deleted", "sub_deleted");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sub_deleted", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("applied", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_DispatchCheckoutSessionCompleted_ToCorrectHandler()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildCheckoutCompletedEventJson("evt_checkout", "cus_checkout", "sub_checkout", "driver@hablotruck.com");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("checkout", fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("applied", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_ReturnOkAndAuditSkippedNoCustomer_WhenParsedEventHasNoCustomer()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildInvoicePaidWithoutCustomerEventJson("evt_no_customer", "sub_no_customer");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("skipped_no_customer", audit.Outcome);
    }

    [Fact]
    public async Task Run_Should_ReturnOkAndAuditSkippedNoCustomer_ForUnhandledEventType()
    {
        var fixture = BuildFixture(eventStoreResult: true);
        var json = BuildUnhandledCustomerCreatedEventJson("evt_unhandled");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(fixture.Handler.Calls);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("skipped_no_customer", audit.Outcome);
        Assert.Equal("customer.created", audit.EventType);
    }

    [Fact]
    public async Task Run_Should_ReturnOk_When_AuditAppendFails()
    {
        const string webhookSecret = "whsec_test_webhook";

        var options = new StripeOptions
        {
            WebhookSecret = webhookSecret,
            StripeSecretKey = "sk_test"
        };

        var signatureValidator = new StripeSignatureValidator(options, NullLogger<StripeSignatureValidator>.Instance);
        var parser = new StripeEventParser();
        var eventStore = new StubStripeEventStore(StripeEventProcessingStartResult.Started);
        var auditStore = new ThrowingStripeEventAuditStore();
        var handler = new RecordingStripeSubscriptionHandler();
        var userResolver = new StaticUserResolver();
        var metrics = new Metrics(new TelemetryClient(new TelemetryConfiguration()));

        var function = new StripeWebhookFunction(
            signatureValidator,
            parser,
            eventStore,
            auditStore,
            handler,
            userResolver,
            metrics,
            NullLogger<StripeWebhookFunction>.Instance);

        var json = BuildInvoicePaidEventJson("evt_audit_throw", "cus_audit_throw", "sub_audit_throw");
        var req = NewSignedRequest(json, webhookSecret);

        var response = await function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("invoice_paid", handler.Calls);
    }

    [Fact]
    public async Task Run_Should_RecordFailedOutcome_When_HandlerThrows()
    {
        var fixture = BuildFixture(new StubStripeEventStore(StripeEventProcessingStartResult.Started));
        fixture.Handler.ThrowOnSubscriptionUpdated = true;

        var json = BuildSubscriptionUpdatedEventJson("evt_throw", "cus_throw", "sub_throw");
        var req = NewSignedRequest(json, fixture.WebhookSecret);

        var response = await fixture.Function.Run(req, req.FunctionContext);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var audit = Assert.Single(fixture.AuditStore.Items);
        Assert.Equal("failed", audit.Outcome);
        Assert.NotNull(audit.Error);
    }

    private static Fixture BuildFixture(bool eventStoreResult)
        => BuildFixture(new StubStripeEventStore(eventStoreResult
            ? StripeEventProcessingStartResult.Started
            : StripeEventProcessingStartResult.AlreadyProcessed));

    private static Fixture BuildFixture(IStripeEventStore eventStore, IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        const string webhookSecret = "whsec_test_webhook";

        var options = new StripeOptions
        {
            WebhookSecret = webhookSecret,
            StripeSecretKey = "sk_test"
        };

        var signatureValidator = new StripeSignatureValidator(options, NullLogger<StripeSignatureValidator>.Instance);
        var parser = new StripeEventParser();
        var auditStore = new RecordingStripeEventAuditStore();
        var handler = new RecordingStripeSubscriptionHandler();
        var userResolver = new StaticUserResolver();
        var metrics = new Metrics(new TelemetryClient(new TelemetryConfiguration()));

        var function = new StripeWebhookFunction(
            signatureValidator,
            parser,
            eventStore,
            auditStore,
            handler,
            userResolver,
            metrics,
            NullLogger<StripeWebhookFunction>.Instance,
            adminPaymentAlerts);

        return new Fixture(function, handler, auditStore, webhookSecret);
    }

    private static TestHttpRequestData NewSignedRequest(string json, string secret)
    {
        var header = CreateStripeSignatureHeader(json, secret, timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return NewRequest(json, header);
    }

    private static string CreateStripeSignatureHeader(string payload, string secret, long timestamp)
    {
        var ts = timestamp.ToString(CultureInfo.InvariantCulture);
        var signature = EventUtility.ComputeSignature(secret, ts, payload);
        return $"t={ts},v1={signature}";
    }

    private static TestHttpRequestData NewRequest(string json, string? stripeSignatureHeader)
    {
        var context = new TestFunctionContext();
        var req = new TestHttpRequestData(context, json);

        if (!string.IsNullOrWhiteSpace(stripeSignatureHeader))
        {
            req.Headers.Add("Stripe-Signature", stripeSignatureHeader);
            Assert.True(req.Headers.TryGetValues("Stripe-Signature", out var values));
            Assert.Contains(stripeSignatureHeader, values!);
        }

        return req;
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
      "cancel_at_period_end": false,
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

    private static string BuildCustomerUpdatedEventJson(string eventId, string customerId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "customer.updated",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_customer_updated",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{customerId}}",
      "object": "customer",
      "invoice_settings": {
        "default_payment_method": "pm_new_123"
      }
    },
    "previous_attributes": {
      "invoice_settings": {
        "default_payment_method": "pm_old_123"
      }
    }
  }
}
""";

    private static string BuildPaymentIntentFailedEventJson(
        string eventId,
        string customerId,
        string paymentIntentId,
        string chargeId,
        string declineCode)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "payment_intent.payment_failed",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_pi_failed",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{paymentIntentId}}",
      "object": "payment_intent",
      "customer": "{{customerId}}",
      "latest_charge": "{{chargeId}}",
      "payment_method": "pm_failed",
      "payment_method_types": ["card"],
      "amount": 10340,
      "currency": "usd",
      "status": "requires_payment_method",
      "last_payment_error": {
        "code": "card_declined",
        "decline_code": "{{declineCode}}",
        "message": "Your card was declined.",
        "payment_method": {
          "id": "pm_failed",
          "object": "payment_method",
          "type": "card"
        }
      }
    }
  }
}
""";

    private static string BuildChargeFailedEventJson(
        string eventId,
        string customerId,
        string paymentIntentId,
        string chargeId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "charge.failed",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_charge_failed",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{chargeId}}",
      "object": "charge",
      "customer": "{{customerId}}",
      "payment_intent": "{{paymentIntentId}}",
      "payment_method": "pm_charge_failed",
      "amount": 10340,
      "currency": "usd",
      "status": "failed",
      "failure_code": "generic_decline",
      "failure_message": "Your card was declined.",
      "outcome": {
        "reason": "generic_decline",
        "type": "issuer_declined"
      },
      "payment_method_details": {
        "type": "card"
      },
      "billing_details": {
        "email": "driver@example.com"
      }
    }
  }
}
""";

    private static string BuildCheckoutExpiredEventJson(string eventId, string checkoutSessionId)
        => $$"""
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "checkout.session.expired",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_checkout_expired",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "{{checkoutSessionId}}",
      "object": "checkout.session",
      "customer": null,
      "customer_details": {
        "email": "driver@example.com"
      },
      "mode": "subscription",
      "status": "expired",
      "amount_total": 10340,
      "currency": "usd"
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
            "quantity": 1,
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

    private static string BuildUnhandledCustomerCreatedEventJson(string eventId)
        => """
{
  "id": "{{eventId}}",
  "object": "event",
  "api_version": "2024-06-20",
  "type": "customer.created",
  "created": 1770000000,
  "livemode": false,
  "pending_webhooks": 1,
  "request": {
    "id": "req_test_unhandled",
    "idempotency_key": null
  },
  "data": {
    "object": {
      "id": "cus_unhandled",
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
    "id": "req_malformed_payload",
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

    private sealed record Fixture(
        StripeWebhookFunction Function,
        RecordingStripeSubscriptionHandler Handler,
        RecordingStripeEventAuditStore AuditStore,
        string WebhookSecret);

    private sealed class StubStripeEventStore : IStripeEventStore
    {
        private readonly StripeEventProcessingStartResult _result;
        public int MarkProcessedCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public StubStripeEventStore(StripeEventProcessingStartResult result)
            => _result = result;

        public Task<StripeEventProcessingStartResult> TryStartProcessingAsync(string stripeEventId, string eventType, DateTimeOffset createdUtc, CancellationToken ct = default)
            => Task.FromResult(_result);

        public Task MarkProcessedAsync(string stripeEventId, CancellationToken ct = default)
        {
            MarkProcessedCalls++;
            return Task.CompletedTask;
        }

        public Task ReleaseProcessingAsync(string stripeEventId, CancellationToken ct = default)
        {
            ReleaseCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class DeduplicatingStripeEventStore : IStripeEventStore
    {
        private readonly HashSet<string> _processed = new(StringComparer.OrdinalIgnoreCase);
        public List<StripeEventProcessingStartResult> StartResults { get; } = new();

        public Task<StripeEventProcessingStartResult> TryStartProcessingAsync(string stripeEventId, string eventType, DateTimeOffset createdUtc, CancellationToken ct = default)
        {
            var firstTime = _processed.Add(stripeEventId.Trim());
            var result = firstTime ? StripeEventProcessingStartResult.Started : StripeEventProcessingStartResult.AlreadyProcessed;
            StartResults.Add(result);
            return Task.FromResult(result);
        }

        public Task MarkProcessedAsync(string stripeEventId, CancellationToken ct = default) => Task.CompletedTask;

        public Task ReleaseProcessingAsync(string stripeEventId, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class ThrowingStripeEventAuditStore : IStripeEventAuditStore
    {
        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AppendAsync(StripeEventAuditItem item, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated_audit_failure");

        public Task<IReadOnlyList<StripeEventAuditItem>> GetByEventIdAsync(string stripeEventId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StripeEventAuditItem>>(Array.Empty<StripeEventAuditItem>());

        public Task<IReadOnlyList<StripeEventAuditItem>> QueryRecentAsync(DateTimeOffset dayUtc, int take = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StripeEventAuditItem>>(Array.Empty<StripeEventAuditItem>());
    }

    private sealed class RecordingStripeEventAuditStore : IStripeEventAuditStore
    {
        public List<StripeEventAuditItem> Items { get; } = new();

        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AppendAsync(StripeEventAuditItem item, CancellationToken ct = default)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StripeEventAuditItem>> GetByEventIdAsync(string stripeEventId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StripeEventAuditItem>>(Items.Where(i => i.StripeEventId == stripeEventId).ToList());

        public Task<IReadOnlyList<StripeEventAuditItem>> QueryRecentAsync(DateTimeOffset dayUtc, int take = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StripeEventAuditItem>>(Items.Take(take).ToList());
    }

    private sealed class RecordingStripeSubscriptionHandler : IStripeSubscriptionHandler
    {
        public List<string> Calls { get; } = new();
        public bool ThrowOnSubscriptionUpdated { get; set; }
        public AccessDecision? SubscriptionUpdatedDecision { get; set; }

        public Task HandleCheckoutCompletedAsync(StripeEventData data, CancellationToken ct = default)
        {
            Calls.Add("checkout");
            return Task.CompletedTask;
        }

        public Task<AccessDecision?> HandleSubscriptionUpdatedAsync(StripeEventData data, CancellationToken ct = default)
        {
            Calls.Add("sub_updated");
            if (ThrowOnSubscriptionUpdated)
                throw new InvalidOperationException("test_throw");

            return Task.FromResult(SubscriptionUpdatedDecision);
        }

        public Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeEventData data, CancellationToken ct = default)
        {
            Calls.Add("sub_deleted");
            return Task.FromResult<AccessDecision?>(null);
        }

        public Task<AccessDecision?> HandleInvoicePaidAsync(StripeEventData data, CancellationToken ct = default)
        {
            Calls.Add("invoice_paid");
            return Task.FromResult<AccessDecision?>(null);
        }

        public Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeEventData data, CancellationToken ct = default)
        {
            Calls.Add("invoice_failed");
            return Task.FromResult<AccessDecision?>(null);
        }

        public Task HandleCustomerUpdatedAsync(StripeEventData data, CancellationToken ct = default)
        {
            Calls.Add("customer_updated");
            return Task.CompletedTask;
        }

        public Task<AccessDecision?> HandleSubscriptionUpdatedAsync(StripeSubscriptionUpdate input, CancellationToken ct = default)
            => Task.FromResult<AccessDecision?>(null);

        public Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeSubscriptionDeleted input, CancellationToken ct = default)
            => Task.FromResult<AccessDecision?>(null);

        public Task<AccessDecision?> HandleInvoicePaidAsync(StripeInvoicePaid input, CancellationToken ct = default)
            => Task.FromResult<AccessDecision?>(null);

        public Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeInvoicePaymentFailed input, CancellationToken ct = default)
            => Task.FromResult<AccessDecision?>(null);
    }

    private sealed class StaticUserResolver : IUserResolver
    {
        public Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(new UserRef("HT_U_000", "U_WEBHOOK"));

        public Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);

        public Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);
    }

    private sealed class RecordingAdminPaymentAlertNotifier : IAdminPaymentAlertNotifier
    {
        private readonly AdminPaymentAlertDeliveryResult _result;

        public RecordingAdminPaymentAlertNotifier(AdminPaymentAlertDeliveryResult result)
            => _result = result;

        public List<AdminPaymentAlert> Alerts { get; } = new();

        public Task<AdminPaymentAlertDeliveryResult> NotifyAsync(AdminPaymentAlert alert, CancellationToken ct = default)
        {
            Alerts.Add(alert);
            return Task.FromResult(_result);
        }
    }

    private sealed class TestHttpRequestData : HttpRequestData
    {
        public TestHttpRequestData(FunctionContext functionContext, string body)
            : base(functionContext)
        {
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            Headers = new HttpHeadersCollection();
        }

        public override Stream Body { get; }
        public override HttpHeadersCollection Headers { get; }
        public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();
        public override Uri Url { get; } = new("https://localhost/api/stripe/webhook");
        public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();
        public override string Method { get; } = "POST";

        public override HttpResponseData CreateResponse()
            => new TestHttpResponseData(FunctionContext);
    }

    private sealed class TestHttpResponseData : HttpResponseData
    {
        public TestHttpResponseData(FunctionContext functionContext)
            : base(functionContext)
        {
            Headers = new HttpHeadersCollection();
            Body = new MemoryStream();
        }

        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; }
        public override Stream Body { get; set; }
        public override HttpCookies Cookies { get; } = null!;
    }

    private sealed class TestFunctionContext : FunctionContext
    {
        public override string InvocationId { get; } = Guid.NewGuid().ToString("N");
        public override string FunctionId { get; } = "StripeWebhookFunction";
        public override TraceContext TraceContext { get; } = null!;
        public override BindingContext BindingContext { get; } = null!;
        public override RetryContext RetryContext { get; } = null!;
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition { get; } = null!;
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features { get; } = null!;
        public override CancellationToken CancellationToken { get; } = CancellationToken.None;
    }
}
