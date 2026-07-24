# Stripe Payment Failure Backend Behavior

Este documento describe como se comporta el backend de HabloTruck cuando Stripe reporta fallos relacionados con pagos.

## Objetivo

- Capturar fallos de pago enviados por Stripe.
- Enviar email admin cuando ocurra un fallo relevante.
- Registrar telemetry/audit suficiente para investigar el incidente en App Insights.
- Evitar guardar datos sensibles de tarjeta, CVC o secretos.

## Eventos Stripe Cubiertos

El webhook procesa explicitamente estos eventos de fallo:

- `invoice.payment_failed`
- `payment_intent.payment_failed`
- `charge.failed`
- `checkout.session.async_payment_failed`
- `checkout.session.expired`

Tambien sigue procesando eventos exitosos o de lifecycle:

- `checkout.session.completed`
- `invoice.paid`
- `customer.updated`
- `customer.subscription.updated`
- `customer.subscription.deleted`

## Diferencia Entre Los Eventos De Fallo

### `invoice.payment_failed`

Representa un fallo asociado al cobro de una factura de suscripcion.

Comportamiento:

- Actualiza el estado local de recovery/pago fallido.
- Puede disparar flujo de recovery en ManyChat.
- Envia alerta admin usando `stripe_invoice_payment_failed`.
- Mantiene la logica existente de acceso/grace/recovery.

### `payment_intent.payment_failed`

Representa un intento de pago fallido a nivel de PaymentIntent.

Comportamiento:

- El backend extrae `PaymentIntentId`, `ChargeId`, `StripeCustomerId`, `declineCode`, `failureCode`, monto y moneda.
- Envia email admin usando `stripe_payment_failure_webhook`.
- No actualiza por si solo el estado de acceso del usuario; es una alerta operacional.

### `charge.failed`

Representa un intento de cargo fallido a nivel de Charge.

Comportamiento:

- El backend extrae `ChargeId`, `PaymentIntentId`, `StripeCustomerId`, `failureCode`, `declineCode`, monto y moneda.
- Envia email admin usando `stripe_payment_failure_webhook`.
- Puede representar el mismo intento fallido que `payment_intent.payment_failed`.

### `checkout.session.async_payment_failed`

Representa un fallo de pago asincronico despues de crear una Checkout Session.

Comportamiento:

- El backend extrae `CheckoutSessionId`, `PaymentIntentId`, `StripeCustomerId`, email, monto y moneda.
- Envia email admin.

### `checkout.session.expired`

Representa una sesion de Checkout expirada.

Importante:

- No necesariamente es un decline de tarjeta.
- Normalmente significa que el cliente no completo el checkout a tiempo.

Comportamiento:

- El backend envia email admin con severidad `High`.
- El `failureCode` usado es `checkout_session_expired`.
- No hay `declineCode` porque no necesariamente hubo intento real de cobro.

## Email Admin

Cuando ocurre un fallo capturado, el backend usa SendGrid para enviar un email admin.

El email incluye:

- Ambiente (`Development`, `Production`, etc.).
- Evento Stripe.
- `StripeEventId`.
- `PaymentIntentId`.
- `ChargeId`.
- `CheckoutSessionId`.
- `StripeCustomerId`.
- Monto y moneda.
- `failureCode`.
- `declineCode`.
- Mensaje de fallo.
- Escenario probable.
- Pasos recomendados para resolver.
- KQL para buscar el incidente en App Insights.

## Outcomes De Alertas

El webhook registra uno de estos outcomes:

- `stripe_payment_failure_alert_sent`
- `stripe_payment_failure_alert_skipped`
- `stripe_payment_failure_alert_failed`

Significado:

- `stripe_payment_failure_alert_sent`: SendGrid acepto el email.
- `stripe_payment_failure_alert_skipped`: el email no se envio por una razon controlada, por ejemplo alerts deshabilitadas o configuracion faltante.
- `stripe_payment_failure_alert_failed`: SendGrid rechazo el request o hubo una excepcion inesperada.

## SendGrid

El notifier de SendGrid devuelve un resultado estructurado:

- `Sent`
- `Skipped`
- `Failed`

Razones comunes:

- `sendgrid_accepted`
- `admin_payment_alert_email_disabled`
- `sendgrid_or_email_settings_missing`
- `sendgrid_provider_rejected`
- `sendgrid_unexpected_exception`

## Duplicados Entre `charge.failed` Y `payment_intent.payment_failed`

Stripe puede enviar ambos eventos para el mismo intento fallido:

- `payment_intent.payment_failed`
- `charge.failed`

Actualmente el backend no deduplica esos dos eventos entre si.

Eso significa que puedes recibir dos emails para el mismo intento fallido.

Para saber si ambos emails pertenecen al mismo intento, compara:

1. `PaymentIntentId`
2. `ChargeId`
3. `StripeCustomerId`
4. `amount`
5. `currency`
6. tiempo de ocurrencia

Regla practica:

```text
Mismo PaymentIntentId = mismo intento de pago
```

Confirmacion fuerte:

```text
Mismo PaymentIntentId + mismo ChargeId = definitivamente el mismo intento fallido
```

Ejemplo:

```text
Email 1
Evento Stripe: payment_intent.payment_failed
PaymentIntentId: pi_123
ChargeId: ch_456

Email 2
Evento Stripe: charge.failed
PaymentIntentId: pi_123
ChargeId: ch_456
```

Conclusion:

```text
No son dos fallos distintos del cliente.
Son dos eventos Stripe diferentes describiendo el mismo fallo.
```

## Que Pasa Si Ambos Eventos Llegan Al Mismo Tiempo

Estado actual:

- Cada evento tiene un `StripeEventId` diferente.
- La idempotencia actual evita procesar el mismo `StripeEventId` dos veces.
- No evita que dos eventos distintos del mismo intento generen dos emails.

Casos:

- Si llega `payment_intent.payment_failed` primero, envia email.
- Si luego llega `charge.failed` con el mismo `PaymentIntentId`, tambien envia email.
- Si llega `charge.failed` primero, envia email.
- Si luego llega `payment_intent.payment_failed` con el mismo `PaymentIntentId`, tambien envia email.
- Si ambos llegan al mismo tiempo, ambos pueden enviar email.

Esto es aceptado por ahora de forma intencional para evitar perder alertas.

## Posible Mejora Futura

Si se decide reducir ruido, se podria agregar deduplicacion logica por intento de pago.

Clave recomendada:

```text
PaymentAttemptKey = PaymentIntentId
```

Fallbacks:

```text
1. PaymentIntentId
2. ChargeId
3. CheckoutSessionId
4. StripeEventId
```

La implementacion correcta requeriria un store atomico, por ejemplo:

```text
PaymentFailureAlertStore
PartitionKey = payment_failure_alert
RowKey = PaymentAttemptKey
Status = processing | sent | failed
FirstStripeEventId
FirstEventType
PaymentIntentId
ChargeId
CreatedUtc
UpdatedUtc
SendGridStatus
FailureReason
```

Regla propuesta:

- Si la key no existe: crear `processing` y enviar email.
- Si SendGrid acepta: marcar `sent`.
- Si SendGrid falla: marcar `failed`.
- Si ya existe `sent`: no enviar segundo email.
- Si ya existe `processing`: no enviar segundo email y registrar duplicado en progreso.
- Si existe `failed`: permitir retry despues de una ventana controlada.

## KQL Para Ver Alertas De Pago Fallidas

```kql
traces
| where timestamp > ago(24h)
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend stripeEventId = tostring(customDimensions["StripeEventId"])
| extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
| extend paymentIntentId = tostring(customDimensions["PaymentIntentId"])
| extend chargeId = tostring(customDimensions["ChargeId"])
| extend checkoutSessionId = tostring(customDimensions["CheckoutSessionId"])
| where outcome in (
    "stripe_payment_failure_alert_sent",
    "stripe_payment_failure_alert_skipped",
    "stripe_payment_failure_alert_failed"
)
| project timestamp, severityLevel, outcome, reason, stripeEventId, stripeCustomerId, paymentIntentId, chargeId, checkoutSessionId, message, customDimensions
| order by timestamp desc
```

## KQL Para Detectar Posibles Duplicados Por PaymentIntent

```kql
traces
| where timestamp > ago(24h)
| extend outcome = tostring(customDimensions["Outcome"])
| extend paymentIntentId = tostring(customDimensions["PaymentIntentId"])
| extend chargeId = tostring(customDimensions["ChargeId"])
| extend eventType = tostring(customDimensions["EventType"])
| where outcome == "stripe_payment_failure_alert_sent"
| where isnotempty(paymentIntentId)
| summarize events=count(), eventTypes=make_set(eventType), chargeIds=make_set(chargeId), firstSeen=min(timestamp), lastSeen=max(timestamp) by paymentIntentId
| where events > 1
| order by lastSeen desc
```

## Configuracion Requerida

Para que el email funcione en el environment correspondiente:

- `AdminPaymentAlerts__Enabled=true`
- `AdminPaymentAlerts__ToEmail=info@rnmglobalsolutions.com`
- `AdminPaymentAlerts__FromEmail=alerts-platform@rnmglobalsolutions.com`
- `AdminPaymentAlerts__SendGridApiKey` configurado como secret/app setting.

En Stripe, el webhook debe tener seleccionados al menos estos eventos:

- `charge.failed`
- `checkout.session.async_payment_failed`
- `checkout.session.completed`
- `checkout.session.expired`
- `customer.updated`
- `customer.subscription.deleted`
- `customer.subscription.updated`
- `invoice.paid`
- `invoice.payment_failed`
- `payment_intent.payment_failed`

## Estado Actual

El backend captura fallos de pago desde Stripe, envia email admin via SendGrid, y deja telemetry/audit para investigacion.

Decision actual:

- Se prefiere recibir alertas potencialmente duplicadas antes que perder un fallo real.
- Los duplicados se identifican comparando `PaymentIntentId` y `ChargeId`.
- La deduplicacion cross-event queda como mejora futura si el volumen de emails duplicados se vuelve molesto.
