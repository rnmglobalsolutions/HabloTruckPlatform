using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Integrations.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Domain.Tests.Infrastructure;

public sealed class SendGridAdminPaymentAlertNotifierTests
{
    [Fact]
    public async Task NotifyAsync_Should_Send_Formatted_Production_Payment_Error_Email()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var sut = new SendGridAdminPaymentAlertNotifier(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.sendgrid.com/") },
            Options.Create(new AdminPaymentAlertEmailOptions
            {
                Enabled = true,
                EnvironmentName = "Production",
                SendGridApiKey = "SG.test",
                ToEmail = "grettadecien@gmail.com",
                FromEmail = "info@rnmglobalsolutions.com",
                FromName = "HabloTruck Production Alerts"
            }),
            NullLogger<SendGridAdminPaymentAlertNotifier>.Instance);

        var result = await sut.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_invoice_payment_failed",
            FailureStage = "subscription_renewal_or_invoice_payment",
            FailureReason = "payment_recovery_started",
            UserId = "U_123",
            Email = "driver@example.com",
            ManyChatSubscriberId = "mc_123",
            StripeCustomerId = "cus_123",
            StripeSubscriptionId = "sub_123",
            StripeEventId = "evt_123",
            PlanType = "individual_monthly",
            PriceId = "price_monthly",
            Details =
            {
                ["stripeEventType"] = "payment_intent.payment_failed",
                ["paymentIntentId"] = "pi_123",
                ["chargeId"] = "ch_123",
                ["failureCode"] = "card_declined",
                ["declineCode"] = "insufficient_funds"
            }
        });

        Assert.Equal(AdminPaymentAlertDeliveryStatus.Sent, result.Status);
        Assert.Equal("sendgrid_accepted", result.Reason);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v3/mail/send", request.Path);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("SG.test", request.AuthorizationParameter);

        using var doc = JsonDocument.Parse(request.Body);
        var root = doc.RootElement;
        var personalization = root.GetProperty("personalizations")[0];
        Assert.Equal("grettadecien@gmail.com", personalization.GetProperty("to")[0].GetProperty("email").GetString());

        var subject = personalization.GetProperty("subject").GetString();
        Assert.Contains("URGENTE: ERROR DE PAGO EN PRODUCTION", subject);
        Assert.Contains("stripe_invoice_payment_failed", subject);

        var html = root.GetProperty("content")[1].GetProperty("value").GetString();
        Assert.Contains("ERROR DE PAGO EN PRODUCTION", html);
        Assert.Contains("driver@example.com", html);
        Assert.Contains("evt_123", html);
        Assert.Contains("insufficient_funds", html);
        Assert.Contains("pi_123", html);
        Assert.Contains("ch_123", html);
        Assert.Contains("KQL Para Localizar El Problema", html);
        Assert.Contains("App Insights", html);
        Assert.Contains("let stripeEventIdParam", html);
        Assert.Contains("isnotempty(paymentIntentIdParam)", html);
        Assert.DoesNotContain("has &quot;&quot;", html);
    }

    [Fact]
    public async Task NotifyAsync_Should_ReturnSkipped_WhenDisabled()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var sut = new SendGridAdminPaymentAlertNotifier(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.sendgrid.com/") },
            Options.Create(new AdminPaymentAlertEmailOptions
            {
                Enabled = false,
                EnvironmentName = "Development",
                SendGridApiKey = "SG.test",
                ToEmail = "grettadecien@gmail.com",
                FromEmail = "info@rnmglobalsolutions.com"
            }),
            NullLogger<SendGridAdminPaymentAlertNotifier>.Instance);

        var result = await sut.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_payment_failure_webhook"
        });

        Assert.Equal(AdminPaymentAlertDeliveryStatus.Skipped, result.Status);
        Assert.Equal("admin_payment_alert_email_disabled", result.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotifyAsync_Should_UseConfiguredEnvironmentName_WhenAlertDoesNotOverrideIt()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var sut = new SendGridAdminPaymentAlertNotifier(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.sendgrid.com/") },
            Options.Create(new AdminPaymentAlertEmailOptions
            {
                Enabled = true,
                EnvironmentName = "Testing",
                SendGridApiKey = "SG.test",
                ToEmail = "grettadecien@gmail.com",
                FromEmail = "info@rnmglobalsolutions.com"
            }),
            NullLogger<SendGridAdminPaymentAlertNotifier>.Instance);

        var result = await sut.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_payment_failure_webhook",
            FailureReason = "insufficient_funds"
        });

        Assert.Equal(AdminPaymentAlertDeliveryStatus.Sent, result.Status);

        var request = Assert.Single(handler.Requests);
        using var doc = JsonDocument.Parse(request.Body);
        var root = doc.RootElement;
        var personalization = root.GetProperty("personalizations")[0];
        var subject = personalization.GetProperty("subject").GetString();
        Assert.Contains("URGENTE: ERROR DE PAGO EN TESTING", subject);

        var html = root.GetProperty("content")[1].GetProperty("value").GetString();
        Assert.Contains("ERROR DE PAGO EN TESTING", html);
    }

    [Fact]
    public async Task NotifyAsync_Should_ReturnFailed_WhenSendGridRejectsRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized);
        var sut = new SendGridAdminPaymentAlertNotifier(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.sendgrid.com/") },
            Options.Create(new AdminPaymentAlertEmailOptions
            {
                Enabled = true,
                EnvironmentName = "Development",
                SendGridApiKey = "SG.bad",
                ToEmail = "grettadecien@gmail.com",
                FromEmail = "info@rnmglobalsolutions.com"
            }),
            NullLogger<SendGridAdminPaymentAlertNotifier>.Instance);

        var result = await sut.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_payment_failure_webhook",
            StripeEventId = "evt_sendgrid_failed"
        });

        Assert.Equal(AdminPaymentAlertDeliveryStatus.Failed, result.Status);
        Assert.Equal("sendgrid_provider_rejected", result.Reason);
        Assert.Equal((int)HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Single(handler.Requests);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public RecordingHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? "",
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(_statusCode);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Body);
}
