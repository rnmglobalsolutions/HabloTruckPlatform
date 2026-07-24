using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Infrastructure.Integrations.Email;

public sealed class SendGridAdminPaymentAlertNotifier : IAdminPaymentAlertNotifier
{
    private readonly HttpClient _http;
    private readonly AdminPaymentAlertEmailOptions _options;
    private readonly ILogger<SendGridAdminPaymentAlertNotifier> _logger;

    public SendGridAdminPaymentAlertNotifier(
        HttpClient http,
        IOptions<AdminPaymentAlertEmailOptions> options,
        ILogger<SendGridAdminPaymentAlertNotifier>? logger = null)
    {
        _http = http;
        _options = options.Value;
        _logger = logger ?? NullLogger<SendGridAdminPaymentAlertNotifier>.Instance;

        _http.BaseAddress ??= new Uri("https://api.sendgrid.com/");
    }

    public async Task<AdminPaymentAlertDeliveryResult> NotifyAsync(AdminPaymentAlert alert, CancellationToken ct = default)
    {
        if (alert is null)
            return AdminPaymentAlertDeliveryResult.Skipped("admin_payment_alert_null");

        alert.EnvironmentName = string.IsNullOrWhiteSpace(alert.EnvironmentName)
            ? _options.EnvironmentName
            : alert.EnvironmentName;

        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} OperationName={OperationName}",
                "decision",
                "admin_payment_alert_email",
                "no_action_needed",
                "admin_payment_alert_email_disabled",
                alert.OperationName);
            return AdminPaymentAlertDeliveryResult.Skipped("admin_payment_alert_email_disabled");
        }

        if (string.IsNullOrWhiteSpace(_options.SendGridApiKey)
            || string.IsNullOrWhiteSpace(_options.ToEmail)
            || string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            _logger.LogWarning(
                "Admin payment alert email skipped. Outcome={Outcome} Reason={Reason} OperationName={OperationName}",
                "configuration_missing",
                "sendgrid_or_email_settings_missing",
                alert.OperationName);
            return AdminPaymentAlertDeliveryResult.Skipped("sendgrid_or_email_settings_missing");
        }

        var subject = BuildSubject(alert);
        var html = BuildHtml(alert);
        var text = BuildText(alert);

        var payload = new
        {
            personalizations = new[]
            {
                new
                {
                    to = new[] { new { email = _options.ToEmail.Trim() } },
                    subject
                }
            },
            from = new
            {
                email = _options.FromEmail.Trim(),
                name = string.IsNullOrWhiteSpace(_options.FromName) ? "HabloTruck Alerts" : _options.FromName.Trim()
            },
            content = new object[]
            {
                new { type = "text/plain", value = text },
                new { type = "text/html", value = html }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SendGridApiKey.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Admin payment alert email failed. Outcome={Outcome} Reason={Reason} OperationName={OperationName}",
                "dependency_failed",
                "sendgrid_unexpected_exception",
                alert.OperationName);
            return AdminPaymentAlertDeliveryResult.Failed("sendgrid_unexpected_exception");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Admin payment alert email sent. Outcome={Outcome} Reason={Reason} OperationName={OperationName} StatusCode={StatusCode}",
                    "completed",
                    "sendgrid_accepted",
                    alert.OperationName,
                    (int)response.StatusCode);
                return AdminPaymentAlertDeliveryResult.Sent("sendgrid_accepted", (int)response.StatusCode);
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Admin payment alert email failed. Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} OperationName={OperationName} ResponseBytes={ResponseBytes}",
                "dependency_failed",
                "sendgrid_provider_rejected",
                (int)response.StatusCode,
                alert.OperationName,
                body.Length);
            return AdminPaymentAlertDeliveryResult.Failed("sendgrid_provider_rejected", (int)response.StatusCode);
        }
    }

    private static string BuildSubject(AdminPaymentAlert alert)
    {
        var env = Upper(alert.EnvironmentName);
        var operation = string.IsNullOrWhiteSpace(alert.OperationName) ? "payment_operation" : alert.OperationName.Trim();
        var reason = string.IsNullOrWhiteSpace(alert.FailureReason) ? "unknown_failure" : alert.FailureReason.Trim();
        return $"URGENTE: ERROR DE PAGO EN {env} - {operation} - {reason}";
    }

    private static string BuildHtml(AdminPaymentAlert alert)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><body style=\"margin:0;background:#f4f6f8;font-family:Arial,Helvetica,sans-serif;color:#17202a;\">");
        sb.AppendLine("<div style=\"max-width:860px;margin:0 auto;padding:24px;\">");
        sb.AppendLine("<div style=\"background:#8b0000;color:#fff;padding:18px 22px;border-radius:8px 8px 0 0;\">");
        sb.AppendLine($"<h1 style=\"margin:0;font-size:22px;letter-spacing:0;\">ERROR DE PAGO EN {E(Upper(alert.EnvironmentName))}</h1>");
        sb.AppendLine($"<p style=\"margin:8px 0 0;font-size:14px;\">{E(alert.OperationName)} - {E(alert.FailureReason)}</p>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div style=\"background:#fff;border:1px solid #d9dee3;border-top:0;padding:22px;border-radius:0 0 8px 8px;\">");

        var declineCode = Detail(alert, "declineCode");
        var failureCode = Detail(alert, "failureCode") ?? Detail(alert, "lastPaymentErrorCode");
        var failureMessage = Detail(alert, "failureMessage") ?? Detail(alert, "lastPaymentErrorMessage");
        var eventType = Detail(alert, "stripeEventType");

        if (!string.IsNullOrWhiteSpace(declineCode) || !string.IsNullOrWhiteSpace(failureCode) || !string.IsNullOrWhiteSpace(eventType))
        {
            sb.AppendLine("<div style=\"border:2px solid #8b0000;background:#fff5f5;padding:14px;margin:0 0 18px;border-radius:6px;\">");
            sb.AppendLine("<h2 style=\"font-size:16px;margin:0 0 8px;color:#8b0000;\">Falla Detectada Por Stripe</h2>");
            sb.AppendLine("<table style=\"border-collapse:collapse;width:100%;font-size:14px;\">");
            Row(sb, "Evento Stripe", eventType);
            Row(sb, "Decline Code", declineCode);
            Row(sb, "Failure Code", failureCode);
            Row(sb, "Mensaje", failureMessage);
            Row(sb, "Escenario Probable", DescribeFailureScenario(eventType, declineCode, failureCode));
            sb.AppendLine("</table>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("<h2 style=\"font-size:18px;margin:0 0 10px;\">Resumen</h2>");
        sb.AppendLine("<table style=\"border-collapse:collapse;width:100%;font-size:14px;\">");
        Row(sb, "Ambiente", alert.EnvironmentName);
        Row(sb, "Severidad", alert.Severity);
        Row(sb, "Fecha UTC", alert.OccurredAtUtc.UtcDateTime.ToString("O"));
        Row(sb, "Etapa", alert.FailureStage);
        Row(sb, "Razon", alert.FailureReason);
        Row(sb, "Operacion", alert.OperationName);
        sb.AppendLine("</table>");

        sb.AppendLine("<h2 style=\"font-size:18px;margin:22px 0 10px;\">Cliente / Usuario</h2>");
        sb.AppendLine("<table style=\"border-collapse:collapse;width:100%;font-size:14px;\">");
        Row(sb, "UserPk", alert.UserPk);
        Row(sb, "UserId", alert.UserId);
        Row(sb, "Email", alert.Email);
        Row(sb, "Telefono", alert.PhoneE164);
        Row(sb, "ManyChat Subscriber", alert.ManyChatSubscriberId);
        Row(sb, "CompanyId", alert.CompanyId);
        sb.AppendLine("</table>");

        sb.AppendLine("<h2 style=\"font-size:18px;margin:22px 0 10px;\">Stripe</h2>");
        sb.AppendLine("<table style=\"border-collapse:collapse;width:100%;font-size:14px;\">");
        Row(sb, "Customer", alert.StripeCustomerId);
        Row(sb, "Subscription", alert.StripeSubscriptionId);
        Row(sb, "Invoice", alert.StripeInvoiceId);
        Row(sb, "Event", alert.StripeEventId);
        Row(sb, "Checkout Session", alert.StripeCheckoutSessionId);
        Row(sb, "PlanType", alert.PlanType);
        Row(sb, "PriceId", alert.PriceId);
        sb.AppendLine("</table>");

        if (alert.Details.Count > 0)
        {
            sb.AppendLine("<h2 style=\"font-size:18px;margin:22px 0 10px;\">Detalles Adicionales</h2>");
            sb.AppendLine("<table style=\"border-collapse:collapse;width:100%;font-size:14px;\">");
            foreach (var detail in alert.Details.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                Row(sb, detail.Key, detail.Value);
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2 style=\"font-size:18px;margin:22px 0 10px;\">Paso A Paso Para Resolver</h2>");
        sb.AppendLine("<ol style=\"font-size:14px;line-height:1.55;padding-left:22px;\">");
        sb.AppendLine("<li>Busca el cliente en Stripe usando Customer, Subscription, Invoice o Checkout Session.</li>");
        sb.AppendLine("<li>Abre la factura/invoice y revisa el decline code, payment intent, metodo de pago y proximo intento de cobro.</li>");
        sb.AppendLine("<li>Si el pago fallo por tarjeta, contacta al cliente desde ManyChat y envia el flujo de actualizar metodo de pago.</li>");
        sb.AppendLine("<li>Si el webhook fallo, revisa Stripe Developers > Webhooks y reenvia el evento despues de corregir la configuracion.</li>");
        sb.AppendLine("<li>En App Insights, usa las KQL de abajo con el StripeEventId, SubscriptionId, CustomerId o la ventana de tiempo del incidente.</li>");
        sb.AppendLine("<li>Verifica que ManyChat tenga HT_BILLING_ACTION_REQUIRED y los campos ht_billing_recovery_* actualizados.</li>");
        sb.AppendLine("</ol>");

        sb.AppendLine("<h2 style=\"font-size:18px;margin:22px 0 10px;\">KQL Para Localizar El Problema</h2>");
        Code(sb, BuildKql(alert));

        sb.AppendLine("</div></div></body></html>");
        return sb.ToString();
    }

    private static string BuildText(AdminPaymentAlert alert)
        => $"""
        ERROR DE PAGO EN {Upper(alert.EnvironmentName)}

        Operacion: {alert.OperationName}
        Razon: {alert.FailureReason}
        Etapa: {alert.FailureStage}
        Fecha UTC: {alert.OccurredAtUtc.UtcDateTime:O}
        Evento Stripe: {Detail(alert, "stripeEventType") ?? "-"}
        Decline Code: {Detail(alert, "declineCode") ?? "-"}
        Failure Code: {Detail(alert, "failureCode") ?? Detail(alert, "lastPaymentErrorCode") ?? "-"}
        Payment Intent: {Detail(alert, "paymentIntentId") ?? "-"}
        Charge: {Detail(alert, "chargeId") ?? "-"}

        Cliente:
        UserId: {alert.UserId ?? "-"}
        Email: {alert.Email ?? "-"}
        Phone: {alert.PhoneE164 ?? "-"}
        ManyChat: {alert.ManyChatSubscriberId ?? "-"}

        Stripe:
        Customer: {alert.StripeCustomerId ?? "-"}
        Subscription: {alert.StripeSubscriptionId ?? "-"}
        Invoice: {alert.StripeInvoiceId ?? "-"}
        Event: {alert.StripeEventId ?? "-"}
        PlanType: {alert.PlanType ?? "-"}
        PriceId: {alert.PriceId ?? "-"}

        Pasos:
        1. Buscar el cliente/factura en Stripe.
        2. Revisar decline code, payment intent y webhook delivery.
        3. Usar App Insights con StripeEventId/SubscriptionId/CustomerId.
        4. Verificar tags y campos de ManyChat.
        """;

    private static string BuildKql(AdminPaymentAlert alert)
    {
        var eventId = EscapeKql(alert.StripeEventId);
        var subId = EscapeKql(alert.StripeSubscriptionId);
        var customerId = EscapeKql(alert.StripeCustomerId);
        var userId = EscapeKql(alert.UserId);
        var paymentIntentId = EscapeKql(Detail(alert, "paymentIntentId"));
        var chargeId = EscapeKql(Detail(alert, "chargeId"));
        var checkoutSessionId = EscapeKql(alert.StripeCheckoutSessionId ?? Detail(alert, "checkoutSessionId"));

        return $"""
        let stripeEventIdParam = "{eventId}";
        let stripeCustomerIdParam = "{customerId}";
        let subscriptionIdParam = "{subId}";
        let userIdParam = "{userId}";
        let paymentIntentIdParam = "{paymentIntentId}";
        let chargeIdParam = "{chargeId}";
        let checkoutSessionIdParam = "{checkoutSessionId}";

        // 1) Ver webhooks y resultado final
        requests
        | where timestamp > ago(24h)
        | where name has "StripeWebhook" or url has "/api/stripe/webhook"
        | extend outcome = tostring(customDimensions["Outcome"])
        | extend reason = tostring(customDimensions["Reason"])
        | project timestamp, name, resultCode, success, duration, outcome, reason, operation_Id, customDimensions
        | order by timestamp desc

        // 2) Trazas del evento/cliente/suscripcion
        traces
        | where timestamp > ago(24h)
        | extend stripeEventId = tostring(customDimensions["StripeEventId"])
        | extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
        | extend subscriptionId = tostring(customDimensions["SubscriptionId"])
        | extend userId = tostring(customDimensions["UserId"])
        | extend paymentIntentId = tostring(customDimensions["PaymentIntentId"])
        | extend chargeId = tostring(customDimensions["ChargeId"])
        | extend checkoutSessionId = tostring(customDimensions["CheckoutSessionId"])
        | where (isnotempty(stripeEventIdParam) and (stripeEventId == stripeEventIdParam or message has stripeEventIdParam))
            or (isnotempty(stripeCustomerIdParam) and (stripeCustomerId == stripeCustomerIdParam or message has stripeCustomerIdParam))
            or (isnotempty(subscriptionIdParam) and (subscriptionId == subscriptionIdParam or message has subscriptionIdParam))
            or (isnotempty(userIdParam) and userId == userIdParam)
            or (isnotempty(paymentIntentIdParam) and (paymentIntentId == paymentIntentIdParam or message has paymentIntentIdParam))
            or (isnotempty(chargeIdParam) and (chargeId == chargeIdParam or message has chargeIdParam))
            or (isnotempty(checkoutSessionIdParam) and (checkoutSessionId == checkoutSessionIdParam or message has checkoutSessionIdParam))
        | project timestamp, severityLevel, message, operation_Id, customDimensions
        | order by timestamp desc

        // 3) Fallas de dependencias: Stripe, ManyChat, Storage
        dependencies
        | where timestamp > ago(24h)
        | where success == false
        | where operation_Id in (
            traces
            | where timestamp > ago(24h)
            | extend stripeEventId = tostring(customDimensions["StripeEventId"])
            | extend subscriptionId = tostring(customDimensions["SubscriptionId"])
            | extend paymentIntentId = tostring(customDimensions["PaymentIntentId"])
            | extend chargeId = tostring(customDimensions["ChargeId"])
            | where (isnotempty(stripeEventIdParam) and stripeEventId == stripeEventIdParam)
                or (isnotempty(subscriptionIdParam) and subscriptionId == subscriptionIdParam)
                or (isnotempty(paymentIntentIdParam) and paymentIntentId == paymentIntentIdParam)
                or (isnotempty(chargeIdParam) and chargeId == chargeIdParam)
            | distinct operation_Id
        )
        | project timestamp, target, name, resultCode, duration, operation_Id, customDimensions
        | order by timestamp desc

        // 4) Eventos Stripe recibidos por tipo
        customMetrics
        | where timestamp > ago(24h)
        | where name == "stripe.events.received"
        | extend eventType = tostring(customDimensions["eventType"])
        | summarize total = sum(value) by eventType
        | order by total desc
        """;
    }

    private static void Row(StringBuilder sb, string label, string? value)
    {
        sb.AppendLine("<tr>");
        sb.AppendLine($"<td style=\"border:1px solid #d9dee3;padding:8px;background:#f7f9fb;width:220px;font-weight:bold;\">{E(label)}</td>");
        sb.AppendLine($"<td style=\"border:1px solid #d9dee3;padding:8px;word-break:break-word;\">{E(string.IsNullOrWhiteSpace(value) ? "-" : value)}</td>");
        sb.AppendLine("</tr>");
    }

    private static void Code(StringBuilder sb, string value)
    {
        sb.AppendLine("<pre style=\"white-space:pre-wrap;background:#101820;color:#f8f8f2;padding:14px;border-radius:6px;font-size:12px;line-height:1.45;overflow:auto;\">");
        sb.AppendLine(E(value));
        sb.AppendLine("</pre>");
    }

    private static string E(string? value)
        => HtmlEncoder.Default.Encode(value ?? "");

    private static string Upper(string? value)
        => string.IsNullOrWhiteSpace(value) ? "PRODUCTION" : value.Trim().ToUpperInvariant();

    private static string EscapeKql(string? value)
        => (value ?? "").Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string? Detail(AdminPaymentAlert alert, string key)
        => alert.Details.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string DescribeFailureScenario(string? eventType, string? declineCode, string? failureCode)
    {
        var code = declineCode ?? failureCode;
        if (string.Equals(eventType, "checkout.session.expired", StringComparison.OrdinalIgnoreCase))
            return "El cliente abandono o no completo Checkout antes de que expirara la sesion.";

        return code switch
        {
            "generic_decline" => "Decline generico del banco emisor.",
            "insufficient_funds" => "Fondos insuficientes en la tarjeta.",
            "lost_card" => "Tarjeta reportada como perdida.",
            "stolen_card" => "Tarjeta reportada como robada.",
            "expired_card" => "Tarjeta expirada.",
            "incorrect_cvc" => "CVC incorrecto.",
            "processing_error" => "Error de procesamiento del proveedor/banco.",
            "incorrect_number" => "Numero de tarjeta incorrecto.",
            "card_velocity_exceeded" => "Limite de velocidad/frecuencia excedido.",
            null or "" => "Stripe no envio un decline code; revisar Payment Intent y Charge.",
            _ => $"Stripe reporto {code}; revisar Payment Intent y Charge para el detalle exacto."
        };
    }
}
