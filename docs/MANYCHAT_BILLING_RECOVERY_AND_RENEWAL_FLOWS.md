# ManyChat Billing Recovery and Renewal Flows

This document describes the ManyChat flows related to:

1. Payment failed or delinquent subscriptions.
2. Renewal reminders.
3. Payment recovery follow-up.
4. Stay with HabloTruck / save before churn.
5. Self-service payment method update.
6. Manual retry of an open Stripe invoice.

It covers:

- What the backend already supports today.
- What ManyChat should do with that support.
- Happy paths.
- Error variants.
- An operational ManyChat blueprint with blocks, exact copy, logic, and backend calls.

## Scope

This document covers:

- renewal reminders
- payment failed journeys
- billing recovery status sync
- Stripe billing portal update-method link
- retry open invoice

It does not cover:

- initial individual checkout
- company seat purchase
- invite-code joins
- post-payment upgrades or downgrades

For post-payment plan changes, see:

- `docs/SUBSCRIPTION_UPGRADES_DOWNGRADES.md`

## Key Backend Reality

The current backend already supports these operational behaviors:

1. It can trigger a ManyChat `payment failed` flow when payment recovery starts.
2. It can queue and send renewal reminders to ManyChat.
3. It can queue and sync billing recovery status fields and tags back to ManyChat.
4. It exposes an endpoint to create a Stripe Billing Portal session so the user can update their payment method.
5. It exposes an endpoint to retry an open Stripe invoice.

Important:

- Some of these interactions are outbound from backend to ManyChat.
- Some are user-initiated from ManyChat back into the backend.
- Outbound ManyChat side effects are queued asynchronously before being processed.

## Relevant Endpoints

- `POST /api/stripe/subscription/payment-method-update-link`
- `POST /api/stripe/subscription/retry-payment`

Relevant backend behaviors:

- `TriggerPaymentFailedFlowAsync`
- `SendSubscriptionReminderAsync`
- `SyncBillingRecoveryStatusAsync`

Related backend files:

- `HabloTruckPlatform/Functions/StripeSubscription_GetPaymentMethodUpdateLink.cs`
- `HabloTruckPlatform/Functions/StripeSubscription_RetryOpenInvoice.cs`
- `HabloTruckPlatform.Application/UseCases/CreateStripePaymentMethodUpdateLinkUseCase.cs`
- `HabloTruckPlatform.Application/UseCases/RetryStripeOpenInvoiceUseCase.cs`
- `HabloTruckPlatform.Application/UseCases/BillingRecoveryManyChatNotifier.cs`
- `HabloTruckPlatform.Application/UseCases/SubscriptionReminderService.cs`
- `HabloTruckPlatform.Application/UseCases/StripeSubscriptionHandler.cs`
- `HabloTruckPlatform.Infrastructure/Integrations/ManyChat/ManyChatSyncClient.cs`

## Request Contracts

## Payment Method Update Link

```json
{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123",
  "returnUrl": "https://<static-website-host>/billing-return.html"
}
```

## Retry Open Invoice

```json
{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123"
}
```

## Headers

```text
Content-Type: application/json
x-api-key: <HttpApiKey>
x-correlation-id: <optional>
```

## Response Contracts

## Payment Method Update Link Success

```json
{
  "result": true,
  "url": "https://billing.stripe.com/...",
  "sessionId": "bps_123",
  "customerId": "cus_123",
  "subscriptionId": "sub_123",
  "immediateRetryRecommended": true,
  "error": null
}
```

## Retry Open Invoice Success

```json
{
  "result": true,
  "customerId": "cus_123",
  "subscriptionId": "sub_123",
  "invoiceId": "in_123",
  "invoiceStatus": "open",
  "collectionMethod": "charge_automatically",
  "invoiceFound": true,
  "paymentAttempted": true,
  "invoicePaid": false,
  "error": null
}
```

## ManyChat State Recommended

Recommended tags and fields already supported by backend:

- `ht_billing_recovery_status`
- `ht_billing_recovery_subscription_id`
- `ht_billing_recovery_invoice_id`
- `ht_billing_recovery_invoice_status`
- `ht_billing_recovery_updated_utc`
- `ht_billing_recovery_started_utc`
- tag `HT_BILLING_ACTION_REQUIRED`
- tag `HT_BILLING_RECOVERED`

Recommended ManyChat-side helper fields:

- `cf_actor_user_pk`
- `cf_actor_user_id`
- `cf_subscription_id`
- `cf_last_billing_error`
- `cf_payment_method_update_url`

## 1. Happy Path: Payment Failed Recovery

## 1.1 Stripe payment fails

### Step 1: Stripe webhook updates backend

The backend detects a delinquent lifecycle and starts payment recovery.

Operationally:

- subscription state is projected
- access may move into grace or degraded state depending on reducer outcome
- payment recovery start is recorded
- backend can trigger ManyChat payment failed flow
- backend syncs billing recovery status back to ManyChat

### Step 2: Backend sends ManyChat recovery signal

If configured, the backend can:

- trigger `PaymentFailedFlowNs`
- sync billing recovery fields and tags

That means ManyChat can receive:

- a visible recovery flow
- or at minimum updated recovery fields/tags

### Step 3: ManyChat shows recovery experience

Recommended visible message:

```text
No pudimos procesar tu pago, pero tu acceso todavía puede recuperarse.
```

Buttons:

- `Actualizar método de pago`
- `Intentar cobro otra vez`
- `Hablar con soporte`

### Step 4: User chooses payment method update

ManyChat calls:

```text
POST /api/stripe/subscription/payment-method-update-link
```

If successful:

- backend returns Stripe Billing Portal URL
- ManyChat opens the portal

### Step 5: User updates payment method in Stripe

The user finishes inside Stripe Billing Portal.

### Step 6: Stripe sends webhook updates again

The backend then:

- updates subscription state
- may close recovery
- may mark recovered
- syncs billing recovery status back to ManyChat

### Step 7: ManyChat shows recovered state

If backend sync marks recovered:

- `HT_BILLING_RECOVERED`
- status fields updated

Recommended visible message:

```text
Listo. Tu pago fue corregido y tu cuenta quedó recuperada.
```

## 2. Happy Path: Renewal Reminder

The backend supports renewal reminders for:

- monthly plans
- yearly plans
- payment recovery follow-up journeys

It also supports a distinct reminder journey for retention:

- `save_before_churn`

### Step 1: Reminder timer runs

The backend evaluates whether a reminder should be sent.

### Step 2: Backend queues reminder dispatch

If a reminder applies:

- backend builds `SubscriptionReminderDispatch`
- backend queues ManyChat reminder dispatch

### Step 3: ManyChat receives reminder flow

Depending on journey and configuration:

- `RenewalReminderFlowNs`
- `SaveBeforeChurnFlowNs`
- `PaymentRecoveryReminderFlowNs`

The user can then:

- continue subscription
- update payment method
- retry payment

## 2.1 Happy Path: Stay with HabloTruck

This is the retention journey that should be shown before the user leaves or churns.

### Step 1: Backend determines save-before-churn reminder applies

The reminder system builds a dispatch with:

- journey = `save_before_churn`

### Step 2: Backend queues ManyChat dispatch

If `SaveBeforeChurnFlowNs` is configured:

- ManyChat receives the retention flow

### Step 3: ManyChat shows retention experience

Recommended visible message:

```text
Antes de irte, quiero recordarte todo lo que ya tienes en HabloTruck.
```

Buttons:

- `Quiero seguir con HabloTruck`
- `Actualizar pago`
- `Hablar con soporte`

### Step 4: User chooses to stay

If the issue is billing:

- route to payment method update or retry

If the issue is hesitation or value perception:

- route back into content and value reinforcement

## 3. Error Variants

## 3.1 Payment failed flow namespace not configured

If `PaymentFailedFlowNs` is empty, the backend cannot send a visible flow automatically.

What still happens:

- recovery status fields can still be synchronized
- ManyChat can still react based on fields or tags

Recommended ManyChat fallback:

- route users from menu or keyword into a billing-recovery flow
- use `Ver mi estado de pago`

## 3.2 Reminder flow namespace not configured

If reminder flow namespaces are missing:

- backend will not send a visible reminder flow
- no visible renewal reminder will appear automatically

Recommended operational decision:

- either configure the flow namespaces
- or do not rely on reminders as a user-visible feature yet

## 3.3 Payment method update link fails

Possible errors:

- `actor_required`
- `actor_not_found`
- `subscription_not_found`
- `forbidden`
- `invalid_return_url`
- `stripe_portal_session_failed`

Recommended ManyChat response:

```text
No pude abrir la página para actualizar tu método de pago en este momento.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

## 3.4 Retry payment fails

Possible errors:

- `actor_required`
- `actor_not_found`
- `subscription_not_found`
- `forbidden`
- `stripe_retry_failed`

Recommended ManyChat response:

```text
No pude volver a intentar el cobro en este momento.
```

Buttons:

- `Actualizar método de pago`
- `Intentar otra vez`
- `Hablar con soporte`

## 3.5 User updates payment method but sees no confirmation

Possible reasons:

- Stripe webhook has not yet completed
- billing recovery status sync is still queued
- the invoice was not yet successfully retried

Recommended ManyChat response:

```text
Ya recibimos tu actualización, pero todavía estamos confirmando el resultado del cobro.
```

Buttons:

- `Verificar estado`
- `Intentar cobro otra vez`

## 3.6 Open invoice exists but payment still does not go through

This is a valid business case.

The retry endpoint may return:

- `invoiceFound = true`
- `paymentAttempted = true`
- `invoicePaid = false`

Recommended ManyChat response:

```text
Intentamos el cobro, pero todavía no se ha completado.
```

Buttons:

- `Actualizar método de pago`
- `Hablar con soporte`

## 4. Other Recommended Scenarios

## 4.1 Keyword-driven billing recovery check

Create keywords such as:

- `pago`
- `mi pago`
- `actualizar pago`
- `actualizar tarjeta`
- `billing`

These should enter the billing recovery menu.

## 4.2 Menu entry for current billing state

ManyChat should expose:

- `Ver mi estado de pago`

This can read the synchronized fields:

- `ht_billing_recovery_status`
- `ht_billing_recovery_invoice_status`

## 4.3 Retry payment after updating method

After the user returns from Stripe Billing Portal, ManyChat should offer:

- `Ya actualicé mi método de pago`

That can route to retry-payment.

## 4.4 Separate recovery from cancellation UX

Do not mix:

- renewal reminder
- payment recovery
- cancellation save flow

They should be different ManyChat flows even if some copy overlaps.

## 4.5 Dedicated "Stay with HabloTruck" positioning

This flow should not sound like a billing error flow.

The user-facing copy should reinforce:

- practical value
- progress already made
- access they would keep
- ease of staying active

## 5. Operational ManyChat Blueprint

This section describes the recommended ManyChat build using flows, blocks, exact copy, logic, and endpoint calls.

## Flow 1 - Pago fallido detectado

### Goal

Handle the main recovery entry point after payment failure.

### Trigger

One of these:

- backend-triggered payment failed flow
- user enters by keyword
- user enters by billing menu

### Block 1.1

Type:

- `Send Message`

Text:

```text
No pudimos procesar tu último pago.

No te preocupes. Vamos a ayudarte a recuperar tu acceso.
```

Buttons:

- `Actualizar método de pago`
- `Intentar cobro otra vez`
- `Ver mi estado`

Actions:

- `Actualizar método de pago` -> `Flow 2 - Crear link de actualización`
- `Intentar cobro otra vez` -> `Flow 4 - Reintentar cobro`
- `Ver mi estado` -> `Flow 5 - Verificar estado de recuperación`

## Flow 2 - Crear link de actualización

### Goal

Create a Stripe Billing Portal session for the user.

### Block 2.1

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/stripe/subscription/payment-method-update-link
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "actorUserPk": "{{cf_actor_user_pk}}",
  "actorUserId": "{{cf_actor_user_id}}",
  "subscriptionId": "{{cf_subscription_id}}",
  "returnUrl": "https://<static-website-host>/billing-return.html"
}
```

Recommended save:

- `cf_payment_method_update_url = response.url`
- `cf_last_billing_error = response.error`

### Block 2.2

Type:

- `Condition`

Logic:

- if `response.result == true`
- and `response.url` exists

If true:

- go to `Flow 3 - Enviar a Stripe Billing Portal`

If false:

- go to `Flow 8 - Error en actualización de pago`

## Flow 3 - Enviar a Stripe Billing Portal

### Goal

Send the user to update their payment method.

### Block 3.1

Type:

- `Send Message`

Text:

```text
Perfecto. Tu enlace para actualizar tu método de pago ya está listo.
```

Button:

- `Actualizar método de pago`

Action:

- open website `{{cf_payment_method_update_url}}`

### Block 3.2

Type:

- `Send Message`

Text:

```text
Cuando termines, regresa aquí y dime:
```

Buttons:

- `Ya actualicé mi método`
- `Tuve un problema`

Actions:

- `Ya actualicé mi método` -> `Flow 4 - Reintentar cobro`
- `Tuve un problema` -> `Flow 8 - Error en actualización de pago`

## Flow 4 - Reintentar cobro

### Goal

Attempt payment retry for the open invoice.

### Block 4.1

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/stripe/subscription/retry-payment
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "actorUserPk": "{{cf_actor_user_pk}}",
  "actorUserId": "{{cf_actor_user_id}}",
  "subscriptionId": "{{cf_subscription_id}}"
}
```

### Block 4.2

Type:

- `Condition`

Logic:

- if `response.result == true`

If true:

- go to `Flow 6 - Resultado del reintento`

If false:

- go to `Flow 9 - Error al reintentar cobro`

## Flow 5 - Verificar estado de recuperación

### Goal

Show the user the current billing-recovery state based on synchronized ManyChat fields.

### Block 5.1

Type:

- `Condition`

Suggested checks:

- `ht_billing_recovery_status`
- `ht_billing_recovery_invoice_status`

### Possible user-facing messages

If status = `recovery_active`:

```text
Tu cuenta sigue en proceso de recuperación.
```

If status = `payment_update_pending_confirmation`:

```text
Ya recibimos tu actualización. Estamos confirmando el resultado del cobro.
```

If status = `payment_retry_failed`:

```text
Todavía no hemos podido completar el cobro.
```

If status = `recovered`:

```text
Tu cuenta ya fue recuperada correctamente.
```

Buttons:

- `Actualizar método de pago`
- `Intentar cobro otra vez`
- `Volver al menú`

## Flow 6 - Resultado del reintento

### Goal

Interpret retry response in a user-friendly way.

### Block 6.1

Type:

- `Condition`

Recommended logic:

- if `response.invoicePaid == true`
  - success
- else if `response.invoiceFound == true`
  - partial progress
- else
  - no open invoice found

### Success message

```text
Perfecto. El cobro se procesó correctamente.
```

### Partial progress message

```text
Intentamos el cobro, pero todavía no se ha completado.
```

### No invoice message

```text
No encontré una factura abierta para reintentar en este momento.
```

Buttons:

- `Verificar estado`
- `Actualizar método de pago`
- `Hablar con soporte`

## Flow 7 - Recordatorio de renovación

### Goal

Support renewal reminder flows triggered from backend.

### Trigger

Backend sends ManyChat reminder flow if configured.

### Suggested message

```text
Tu suscripción de HabloTruck se renovará pronto.
```

Buttons:

- `Seguir con mi plan`
- `Actualizar método de pago`
- `Hablar con soporte`

If the reminder is part of a payment recovery journey:

- route into `Flow 1 - Pago fallido detectado`

## Flow 8 - Stay with HabloTruck

### Goal

Handle save-before-churn retention messaging.

### Trigger

Backend sends ManyChat reminder flow with journey:

- `save_before_churn`

### Block 8.1

Type:

- `Send Message`

Text:

```text
Antes de irte, recuerda que HabloTruck te ayuda a practicar el inglés real que usas en la carretera.
```

### Block 8.2

Type:

- `Send Message`

Text:

```text
Si quieres, te ayudo a mantener tu acceso activo ahora mismo.
```

Buttons:

- `Quiero seguir con HabloTruck`
- `Actualizar pago`
- `Hablar con soporte`

Actions:

- `Quiero seguir con HabloTruck` -> content or account flow
- `Actualizar pago` -> `Flow 2 - Crear link de actualización`
- `Hablar con soporte` -> support flow

## Flow 9 - Error en actualización de pago

### Goal

Handle billing portal session creation failure or user trouble updating payment method.

### Block 8.1

Type:

- `Send Message`

Text:

```text
No pude abrir la página para actualizar tu método de pago en este momento.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

## Flow 10 - Error al reintentar cobro

### Goal

Handle retry-payment endpoint failures.

### Block 9.1

Type:

- `Send Message`

Text:

```text
No pude volver a intentar el cobro en este momento.
```

Buttons:

- `Actualizar método de pago`
- `Intentar otra vez`
- `Hablar con soporte`

## Recommended Minimum Flow Set

For billing recovery and reminders, the minimum ManyChat setup should include:

1. `Pago fallido detectado`
2. `Crear link de actualización`
3. `Enviar a Stripe Billing Portal`
4. `Reintentar cobro`
5. `Verificar estado de recuperación`
6. `Resultado del reintento`
7. `Recordatorio de renovación`
8. `Stay with HabloTruck`
9. `Error en actualización de pago`
10. `Error al reintentar cobro`

## Key Operational Conclusion

With the current backend, the most reliable recovery design is:

1. Stripe failure starts recovery in backend.
2. Backend updates ManyChat recovery state and can trigger a visible recovery flow.
3. User updates payment method through Stripe Billing Portal.
4. User can reattempt payment from ManyChat.
5. Stripe webhook projects final status.
6. Backend syncs recovery outcome back to ManyChat.

That gives you a clean recovery experience without forcing support to manually intervene in every failed-payment case.
