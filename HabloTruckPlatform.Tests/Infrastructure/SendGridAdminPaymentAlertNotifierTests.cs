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
                ToEmail = "info@rnmglobalsolutions.com",
                FromEmail = "alerts-platform@rnmglobalsolutions.com",
                FromName = "HabloTruck Production Alerts"
            }),
            NullLogger<SendGridAdminPaymentAlertNotifier>.Instance);

        await sut.NotifyAsync(new AdminPaymentAlert
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
            PriceId = "price_monthly"
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v3/mail/send", request.Path);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("SG.test", request.AuthorizationParameter);

        using var doc = JsonDocument.Parse(request.Body);
        var root = doc.RootElement;
        var personalization = root.GetProperty("personalizations")[0];
        Assert.Equal("info@rnmglobalsolutions.com", personalization.GetProperty("to")[0].GetProperty("email").GetString());

        var subject = personalization.GetProperty("subject").GetString();
        Assert.Contains("URGENTE: ERROR DE PAGO EN PRODUCTION", subject);
        Assert.Contains("stripe_invoice_payment_failed", subject);

        var html = root.GetProperty("content")[1].GetProperty("value").GetString();
        Assert.Contains("ERROR DE PAGO EN PRODUCTION", html);
        Assert.Contains("driver@example.com", html);
        Assert.Contains("evt_123", html);
        Assert.Contains("KQL Para Localizar El Problema", html);
        Assert.Contains("App Insights", html);
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
