# ManyChat Individual Subscription Flows

This document describes the end-to-end ManyChat flow for HabloTruck individual subscriptions only:

1. Monthly individual subscription.
2. Yearly individual subscription.

It covers:

- Happy path.
- Error variants.
- Additional recommended scenarios.
- An operational ManyChat blueprint with blocks, exact copy, logic, and backend calls.

## Scope

This document only covers:

- `individual_monthly`
- `individual_yearly`

It does not cover:

- company seats
- invite codes
- company join

## Key Backend Reality

The current backend already supports the full checkout and subscription projection for individual monthly and yearly subscriptions.

What happens operationally is:

1. ManyChat calls `POST /api/stripe/payment-link`.
2. The backend creates a Stripe Checkout session and returns a payment URL.
3. The user completes payment in Stripe.
4. Stripe sends webhook events to the backend.
5. The backend projects the subscription and recalculates access.
6. The backend synchronizes access state back to ManyChat asynchronously.

Important:

- The backend currently synchronizes access state back to ManyChat mainly through tags and custom fields.
- The backend does not currently send a dedicated visible "subscription success" flow to the user after an individual successful checkout.
- Because of that, ManyChat should include a `Verificar mi acceso` flow after payment.

## Relevant Endpoints

- `POST /api/stripe/payment-link`

Related backend files:

- `HabloTruckPlatform/Functions/StripePayment_GetPaymentLink.cs`
- `HabloTruckPlatform.Application/UseCases/StripeCheckoutHandler.cs`
- `HabloTruckPlatform.Application/UseCases/StripeSubscriptionHandler.cs`
- `HabloTruckPlatform.Infrastructure/Integrations/ManyChat/ManyChatSyncClient.cs`

## Request Contract

### Monthly

```json
{
  "planType": "individual_monthly",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "manyChatChannel": "facebook",
  "successUrl": "https://<static-website-host>/success.html",
  "cancelUrl": "https://<static-website-host>/cancel.html",
  "quantity": 1
}
```

### Yearly

```json
{
  "planType": "individual_yearly",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "manyChatChannel": "facebook",
  "successUrl": "https://<static-website-host>/success.html",
  "cancelUrl": "https://<static-website-host>/cancel.html",
  "quantity": 1
}
```

### Headers

```text
Content-Type: application/json
x-api-key: <HttpApiKey>
```

## Response Contract

### Success

```json
{
  "result": true,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_...",
  "error": null
}
```

### Validation or operational failure

```json
{
  "result": false,
  "url": null,
  "sessionId": null,
  "error": "..."
}
```

Possible error shapes include:

- validation failures from the request
- `stripe_checkout_session_create_failed`
- `internal_server_error`
- `redirect_url_invalid`
- `redirect_url_must_use_https`
- `redirect_url_host_not_allowed`

Operational note:

- `successUrl` and `cancelUrl` should use the static website deployed with this solution, or another host explicitly included in `Stripe__AllowedCheckoutRedirectHosts`.

## ManyChat State Recommended

Recommended custom fields:

- `cf_plan_type`
- `cf_phone_e164`
- `cf_checkout_url`
- `cf_checkout_session_id`
- `cf_last_checkout_error`

Recommended tags or fields already synchronized by backend:

- `HT_ACCESS_FULL`
- `HT_ACCESS_GRACE`
- `HT_ACCESS_BLOCKED`
- `HT_SRC_INDIVIDUAL`
- custom field `ht_access_mode`

## 1. Happy Path: Monthly or Yearly

The monthly and yearly happy path are operationally the same. The only difference is `planType`.

### Step 1: User chooses plan in ManyChat

ManyChat presents:

- `Plan Mensual`
- `Plan Anual`

Actions:

- monthly -> `cf_plan_type = individual_monthly`
- yearly -> `cf_plan_type = individual_yearly`

### Step 2: ManyChat collects user data

ManyChat should collect:

- email
- optional phone
- `{{contact.id}}` as `manyChatSubscriberId`
- optional `manyChatChannel` such as `facebook`, `instagram`, or `whatsapp`

### Step 3: ManyChat requests Stripe checkout session

ManyChat calls:

```text
POST /api/stripe/payment-link
```

If successful:

- backend returns `result = true`
- ManyChat receives `url`
- ManyChat sends the user to Stripe

### Step 4: User completes checkout in Stripe

The payment and subscription are created in Stripe.

### Step 5: Stripe notifies backend by webhook

The backend processes `checkout.session.completed` and related subscription events.

The backend then:

- resolves or creates the user
- stores Stripe customer/subscription references
- projects individual subscription state
- recalculates access
- queues ManyChat access synchronization

### Step 6: Backend updates ManyChat access state

What returns to ManyChat today is primarily:

- tags
- custom fields

This is not currently a visible success message to the end user.

### Step 7: ManyChat verifies access and closes the loop

Because there is no dedicated automatic visible success flow today, ManyChat should offer:

- `Ya pagué`
- `Verificar mi acceso`

Then ManyChat checks whether the user now has:

- tag `HT_ACCESS_FULL`
- or `ht_access_mode = full`

If yes:

- show activation success
- route into HabloTruck full-access experience

## 2. Error Variants

## 2.1 Communication failure between ManyChat and backend

This happens before Stripe checkout is created.

Examples:

- timeout
- transient network failure
- ManyChat external request error

Recommended ManyChat response:

```text
No pude generar tu enlace de pago en este momento.

Puede ser un problema temporal de conexión.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

## 2.2 Backend validation error

Examples:

- missing `successUrl`
- missing `cancelUrl`
- invalid `planType`
- invalid quantity
- price ID not configured

Recommended ManyChat response:

```text
No pude preparar tu pago correctamente.

Vamos a intentarlo otra vez.
```

Buttons:

- `Intentar otra vez`
- `Cambiar de plan`
- `Hablar con soporte`

## 2.3 Stripe session creation error

This happens when the backend successfully receives the request but Stripe fails creating the checkout session.

Recommended ManyChat response:

```text
Stripe no pudo generar tu enlace de pago ahora mismo.

Puede ser algo temporal.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

## 2.4 User abandons or cancels Stripe checkout

This is not necessarily a system failure.

Recommended ManyChat response:

```text
Tu pago no se completó.

Si quieres, puedes intentarlo otra vez o cambiar de plan.
```

Buttons:

- `Intentar otra vez`
- `Ver plan mensual`
- `Ver plan anual`
- `Hablar con soporte`

## 2.5 User subscribes but receives no response afterward

This is one of the most important UX cases.

Possible reasons:

- webhook is still processing
- ManyChat sync is still queued
- access was already updated but no visible success message exists
- the user expected an automatic confirmation inside chat

Recommended ManyChat response:

```text
Si ya terminaste tu pago, voy a revisar si tu acceso ya fue activado.
```

Buttons:

- `Verificar mi acceso`
- `Todavía no pagué`

## 2.6 Stripe webhook delayed or failed

Here the user may have paid but the system has not yet projected the final subscription state.

Recommended ManyChat response:

```text
Estamos confirmando tu pago.

Normalmente tarda poco. Inténtalo otra vez en breve.
```

Buttons:

- `Volver a verificar`
- `Hablar con soporte`

## 3. Other Recommended Scenarios

## 3.1 Prevent duplicate purchase attempts

Before offering a new individual checkout, ManyChat should verify whether the user already has:

- `HT_ACCESS_FULL`

If yes:

```text
Tu acceso ya está activo. No necesitas volver a pagar.
```

## 3.2 User changes mind between monthly and yearly before paying

This is simple:

- update `cf_plan_type`
- call the same checkout-link endpoint again

## 3.3 User paid with a different Stripe email

To reduce mismatch risk, ManyChat should always send:

- `email`
- `manyChatSubscriberId`
- `phoneE164` when available

This gives the backend more ways to resolve the same user correctly.

## 3.4 Renewal or later payment failure

This is not part of initial checkout, but it matters for monthly and yearly subscriptions.

The backend already supports payment-failed ManyChat recovery flows if configured.

Operationally:

- Stripe payment fails
- backend updates subscription state
- backend can trigger configured ManyChat payment recovery flow

## 4. Operational ManyChat Blueprint

This section describes the recommended ManyChat build using flows, blocks, exact copy, logic, and endpoint calls.

## Flow 1 - Elegir plan individual

### Goal

Let the user choose monthly or yearly.

### Block 1.1

Type:

- `Send Message`

Text:

```text
Para activar tu acceso completo a HabloTruck, elige el plan que prefieras.
```

Buttons:

- `Plan Mensual`
- `Plan Anual`

Actions:

- `Plan Mensual`
  - set `cf_plan_type = individual_monthly`
  - go to `Flow 2 - Recoger datos`
- `Plan Anual`
  - set `cf_plan_type = individual_yearly`
  - go to `Flow 2 - Recoger datos`

## Flow 2 - Recoger datos

### Goal

Collect the minimum information needed to create checkout.

### Block 2.1

Type:

- `User Input`

Prompt:

```text
Escribe tu email para activar tu acceso.
```

Save to:

- ManyChat email or `cf_email`

### Block 2.2

Type:

- `User Input`

Prompt:

```text
Si quieres, escribe tu número de teléfono.
```

Save to:

- `cf_phone_e164`

### Block 2.3

Type:

- `Condition`

Logic:

- if `cf_plan_type` exists
- and email exists

If true:

- go to `Flow 3 - Crear checkout`

If false:

- return to missing-input block

## Flow 3 - Crear checkout

### Goal

Call backend and obtain Stripe checkout link.

### Block 3.1

Type:

- `Send Message`

Text:

```text
Estoy preparando tu enlace de pago seguro.
```

### Block 3.2

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/stripe/payment-link
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "planType": "{{cf_plan_type}}",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "manyChatChannel": "facebook",
  "successUrl": "https://<static-website-host>/success.html",
  "cancelUrl": "https://<static-website-host>/cancel.html",
  "quantity": 1
}
```

Recommended saves:

- `cf_checkout_url = response.url`
- `cf_checkout_session_id = response.sessionId`
- `cf_last_checkout_error = response.error`

### Block 3.3

Type:

- `Condition`

Logic:

- if `response.result == true`
- and `response.url` is present

If true:

- go to `Flow 4 - Enviar a Stripe`

If false:

- go to `Flow 7 - Error al crear checkout`

## Flow 4 - Enviar a Stripe

### Goal

Send the user to checkout and explain what to do afterward.

### Block 4.1

Type:

- `Send Message`

Text:

```text
Perfecto. Tu enlace de pago ya está listo.

Toca el botón de abajo para completar tu suscripción en Stripe.
Cuando termines el pago, regresa aquí y te ayudo a verificar tu acceso.
```

Button:

- `Ir al pago`

Button action:

- open website `{{cf_checkout_url}}`

### Block 4.2

Type:

- `Send Message`

Text:

```text
Cuando termines, toca aquí:
```

Buttons:

- `Ya pagué`
- `Tuve un problema`

Actions:

- `Ya pagué` -> `Flow 5 - Verificar acceso`
- `Tuve un problema` -> `Flow 8 - Pago cancelado o sin respuesta`

## Flow 5 - Verificar acceso

### Goal

Close the loop after payment using ManyChat-visible access state.

### Block 5.1

Type:

- `Send Message`

Text:

```text
Estoy verificando tu acceso. Esto puede tardar un momento.
```

### Block 5.2

Type:

- `Smart Delay`

Suggested delay:

- 10 to 20 seconds

### Block 5.3

Type:

- `Condition`

Logic:

- if tag `HT_ACCESS_FULL` exists
- or `ht_access_mode = full`

If true:

- go to `Flow 6 - Acceso activado`

If false:

- go to `Block 5.4`

### Block 5.4

Type:

- `Send Message`

Text:

```text
Todavía no veo tu acceso activo.

Tu pago puede tardar un poco en reflejarse. Inténtalo otra vez en 1 minuto.
```

Buttons:

- `Volver a verificar`
- `Hablar con soporte`

Actions:

- `Volver a verificar` -> restart `Flow 5`
- `Hablar con soporte` -> support flow

## Flow 6 - Acceso activado

### Goal

Confirm successful activation and route user into full-access experience.

### Block 6.1

Type:

- `Send Message`

Text:

```text
Listo. Tu acceso completo a HabloTruck ya está activo.
```

Buttons:

- `Empezar ahora`
- `Ver temas`
- `Escuchar frases`

## Flow 7 - Error al crear checkout

### Goal

Handle failures before the user reaches Stripe.

### Block 7.1

Type:

- `Send Message`

Text:

```text
No pude preparar tu enlace de pago en este momento.

Puede ser un problema temporal de conexión o de configuración.
```

Buttons:

- `Intentar otra vez`
- `Cambiar de plan`
- `Hablar con soporte`

Actions:

- `Intentar otra vez` -> return to `Flow 3`
- `Cambiar de plan` -> return to `Flow 1`
- `Hablar con soporte` -> support flow

## Flow 8 - Pago cancelado o sin respuesta

### Goal

Handle cancellation, abandonment, or user uncertainty.

### Block 8.1

Type:

- `Send Message`

Text:

```text
Si no pudiste terminar el pago, no te preocupes.

Puedes intentarlo otra vez o cambiar de plan.
```

Buttons:

- `Intentar otra vez`
- `Ver plan anual`
- `Ver plan mensual`
- `Hablar con soporte`

Actions:

- `Intentar otra vez` -> `Flow 3`
- `Ver plan anual` -> set `cf_plan_type = individual_yearly` -> `Flow 3`
- `Ver plan mensual` -> set `cf_plan_type = individual_monthly` -> `Flow 3`
- `Hablar con soporte` -> support flow

## Flow 9 - Ya pagué pero no recibí respuesta

### Goal

Handle the worst UX case: the user believes they paid and got no clear answer.

### Trigger suggestions

- keyword `ya pagué`
- keyword `pague`
- keyword `pagué`
- keyword `mi acceso`
- button from previous flow

### Block 9.1

Type:

- `Send Message`

Text:

```text
Si ya completaste tu pago, voy a revisar si tu acceso ya fue activado.
```

Buttons:

- `Verificar mi acceso`
- `Todavía no pagué`

Actions:

- `Verificar mi acceso` -> `Flow 5`
- `Todavía no pagué` -> `Flow 1`

## Recommended Minimum Flow Set

For monthly and yearly subscriptions, the minimum ManyChat setup should include:

1. `Elegir plan individual`
2. `Recoger datos`
3. `Crear checkout`
4. `Enviar a Stripe`
5. `Verificar acceso`
6. `Acceso activado`
7. `Error al crear checkout`
8. `Pago cancelado o sin respuesta`
9. `Ya pagué pero no recibí respuesta`

## Key Operational Conclusion

With the current backend, the correct operational experience is:

1. ManyChat generates checkout link.
2. User pays in Stripe.
3. Stripe webhook activates subscription in backend.
4. Backend synchronizes access state to ManyChat.
5. ManyChat verifies access and displays visible confirmation.

That is the most reliable current design for individual monthly and yearly subscriptions.
