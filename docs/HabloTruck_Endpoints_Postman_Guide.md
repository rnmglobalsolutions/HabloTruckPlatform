# HabloTruck Endpoints Guide

Este documento lista todos los endpoints HTTP encontrados en la solución con formato orientado a Postman.

Nota general:

- Base URL sugerida: `https://<func>.azurewebsites.net/api`
- Ejemplo `dev`: `https://HabloTruckPlatform-development.azurewebsites.net/api`
- La mayoría de endpoints usan Azure Functions `AuthorizationLevel.Function`, por lo que normalmente se llaman con `?code=<FUNCTION_KEY>` en la URL.
- Además, casi todos validan `x-api-key` por header, incluyendo `health`.
- Header opcional de observabilidad: `x-correlation-id`
- Para `successUrl` y `cancelUrl` de Stripe checkout, el backend ahora valida que el host esté permitido. En la práctica, usa las URLs del static website que despliega esta misma solución (`success.html`, `cancel.html`, `company-success.html`, `company-cancel.html`).

---

## 1. Health

**Nombre**  
`GET - Health`

**Endpoint Url**  
`GET https://<func>.azurewebsites.net/api/health`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
```

**Request Object**  
```text
No request body.
```

**Response Object**  
```text
OK
```

Notas:

- Este endpoint usa ruta anónima, por eso no necesita `?code=<FUNCTION_KEY>`.
- Aun así, la implementación actual sí valida `x-api-key`.
- Si el API key falta o es inválido, la respuesta típica será `401` o `403`.

---

## 2. CompanyInviteFunction

**Nombre**  
`GET/POST - CompanyInviteFunction (Legacy Placeholder)`

**Endpoint Url**  
`GET|POST https://<func>.azurewebsites.net/api/CompanyInviteFunction?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
```

**Request Object**  
```text
No request contract definido.
```

**Response Object**  
```text
Welcome to Azure Functions!
```

---

## 3. Create Invite

**Nombre**  
`POST - Create Invite`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/company/invite?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "companyId": "C1",
  "entitlementId": "E1",
  "maxUses": 25,
  "expiresInDays": 30,
  "createdBy": "admin@empresa.com",
  "code": "HT-AB12CD"
}
```

**Response Object**  
```json
{
  "ok": true,
  "code": "HT-AB12CD",
  "companyId": "C1",
  "entitlementId": "E1",
  "maxUses": 25,
  "expiresAtUtc": "2026-04-30T00:00:00.0000000Z"
}
```

---

## 4. Disable Invite

**Nombre**  
`POST - Disable Invite`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/company/invite/disable?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "inviteCode": "HT-AB12CD"
}
```

**Response Object**  
```json
{
  "ok": true,
  "code": "HT-AB12CD",
  "status": "disabled"
}
```

---

## 5. Get Active Invite

**Nombre**  
`GET/POST - Get Active Invite`

**Endpoint Url**  
`GET|POST https://<func>.azurewebsites.net/api/company/invite/active?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "companyId": "C1",
  "stripeCustomerId": "cus_123",
  "entitlementId": "E1"
}
```

Para `GET`, los mismos valores pueden ir como query string.

`companyId` es el criterio más común. `stripeCustomerId` y `entitlementId` son opcionales.

**Response Object**  
```json
{
  "ok": true,
  "found": true,
  "valid": true,
  "error": null,
  "companyId": "C1",
  "entitlementId": "E1",
  "code": "HT-AB12CD",
  "status": "active",
  "createdBy": "system:fleet_checkout_auto",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16,
  "createdAtUtc": "2026-03-31T12:00:00.0000000Z",
  "expiresAtUtc": ""
}
```

Respuesta de no encontrado típica:
```json
{
  "ok": false,
  "found": false,
  "valid": false,
  "error": "active_invite_not_found"
}
```

---

## 6. Get Invite Info

**Nombre**  
`POST - Get Invite Info`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/company/invite/info?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "inviteCode": "HT-AB12CD"
}
```

**Response Object**  
```json
{
  "ok": true,
  "valid": true,
  "code": "HT-AB12CD",
  "status": "active",
  "companyId": "C1",
  "entitlementId": "E1",
  "expiresAtUtc": "2026-04-30T00:00:00.0000000Z",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16
}
```

---

## 7. Join Company

**Nombre**  
`POST - Join Company`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/company/join?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "inviteCode": "HT-AB12CD",
  "manyChatSubscriberId": "123456789",
  "manyChatChannel": "whatsapp",
  "email": "chofer@example.com",
  "phoneE164": "+17865550100"
}
```

**Response Object**  
```json
{
  "ok": true,
  "alreadyJoined": false,
  "companyId": "C1",
  "entitlementId": "E1"
}
```

Respuesta alternativa típica:
```json
{
  "ok": true,
  "alreadyJoined": true,
  "companyId": "C1",
  "entitlementId": "E1"
}
```

Respuesta de error típica:
```json
{
  "ok": false,
  "error": "no_seats_available",
  "message": "No seats available for this entitlement."
}
```

---

## 8. List Failed Actions

**Nombre**  
`GET - List Failed Actions`

**Endpoint Url**  
`GET https://<func>.azurewebsites.net/api/admin/failed-actions?status=dead&lookbackHours=48&take=200&code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
```

**Request Object**  
```json
{
  "query": {
    "status": "dead",
    "lookbackHours": 48,
    "take": 200
  }
}
```

**Response Object**  
```json
{
  "ok": true,
  "status": "dead",
  "lookbackHours": 48,
  "take": 200,
  "items": [
    {
      "pk": "FA_20260331",
      "rk": "01JQ...",
      "actionType": "manychat_sync",
      "attempts": 3,
      "nextRetryUtc": "2026-03-31T23:00:00.0000000Z",
      "payload": "{\"subscriberId\":\"123\",\"reason\":\"timeout\"}"
    }
  ]
}
```

---

## 9. List Invites For Company

**Nombre**  
`GET/POST - List Invites For Company`

**Endpoint Url**  
`GET|POST https://<func>.azurewebsites.net/api/company/invite/list?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "companyId": "C1",
  "take": 50
}
```

Para `GET`, usar query string:
```text
?companyId=C1&take=50
```

**Response Object**  
```json
{
  "ok": true,
  "companyId": "C1",
  "items": [
    {
      "code": "HT-AB12CD",
      "status": "active",
      "createdAtUtc": "2026-03-31T12:00:00.0000000Z",
      "expiresAtUtc": "",
      "maxUses": 25,
      "uses": 3,
      "remaining": 22,
      "valid": true
    }
  ]
}
```

---

## 10. Requeue Failed Action

**Nombre**  
`POST - Requeue Failed Action`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/admin/failed-actions/requeue?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "pk": "FA_20260331",
  "rk": "01JQ...",
  "delayMinutes": 1
}
```

**Response Object**  
```json
{
  "ok": true,
  "pk": "FA_20260331",
  "rk": "01JQ...",
  "nextRetryUtc": "2026-03-31T23:05:00.0000000Z"
}
```

---

## 11. Resend Admin Invite

**Nombre**  
`POST - Resend Admin Invite`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/company/invite/resend?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "companyId": "C1",
  "stripeCustomerId": "cus_123",
  "entitlementId": "E1"
}
```

**Response Object**  
```json
{
  "ok": true,
  "found": true,
  "created": false,
  "resent": true,
  "updated": false,
  "valid": true,
  "error": null,
  "companyId": "C1",
  "entitlementId": "E1",
  "code": "HT-AB12CD",
  "status": "active",
  "createdBy": "system:fleet_checkout_auto",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16,
  "createdAtUtc": "2026-03-31T12:00:00.0000000Z",
  "expiresAtUtc": ""
}
```

Respuesta de no encontrado típica:
```json
{
  "ok": false,
  "found": false,
  "created": false,
  "resent": false,
  "updated": false,
  "valid": false,
  "error": "active_invite_not_found"
}
```

---

## 12. Start Fleet Checkout

**Nombre**  
`POST - Start Fleet Checkout`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/company/fleet/start-checkout?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

**Request Object**  
```json
{
  "companyId": "C1",
  "companyName": "Acme Trucking",
  "email": "owner@acme.com",
  "phoneE164": "+17865550100",
  "manyChatSubscriberId": "123456789",
  "manyChatChannel": "facebook",
  "seats": 20,
  "successUrl": "https://<static-website-host>/company-success.html",
  "cancelUrl": "https://<static-website-host>/company-cancel.html"
}
```

`companyId` es opcional. Si no se envía, el backend genera o resuelve uno para el checkout administrativo.
Usa un host permitido por `Stripe__AllowedCheckoutRedirectHosts`; por defecto el workflow registra el host del static website del mismo ambiente.

**Response Object**  
```json
{
  "ok": true,
  "error": null,
  "companyId": "C_01JQ...",
  "companyName": "Acme Trucking",
  "seats": 20,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_test_fleet_123"
}
```

---

## 13. Stripe Payment Link

**Nombre**  
`POST - Stripe Payment Link`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/stripe/payment-link?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "planType": "individual_monthly",
  "email": "chofer@example.com",
  "phoneE164": "+17865550100",
  "manyChatSubscriberId": "123456789",
  "manyChatChannel": "instagram",
  "companyId": "C1",
  "companyName": "Acme Trucking",
  "schoolId": "S1",
  "cohortId": "COHORT_01",
  "seats": 0,
  "durationDays": 0,
  "successUrl": "https://<static-website-host>/success.html",
  "cancelUrl": "https://<static-website-host>/cancel.html",
  "quantity": 1
}
```

Nota:

- Si `successUrl` o `cancelUrl` usan un host no permitido, el backend responde con errores tipo `redirect_url_host_not_allowed` o `redirect_url_must_use_https`.
- `generatedAtUtc` captura el instante exacto en UTC en que el payment link fue generado.
- Si guardas este valor en ManyChat como **Text field**, se conserva como texto UTC y ManyChat no lo convierte automáticamente a horario local.
- Si quieres comportamiento de fecha/hora según timezone en ManyChat, usa un campo o automatización de **Date/Time** en vez de un campo de texto.

**Response Object**  
```json
{
  "result": true,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_test_123",
  "generatedAtUtc": "2026-04-10T03:12:45.1234567Z",
  "error": null
}
```

---

## 14. Stripe Replay Event

**Nombre**  
`POST - Stripe Replay Event`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/admin/stripe/replay?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "eventId": "evt_123",
  "eventType": "checkout.session.completed"
}
```

**Response Object**  
```text
Replay executed.
```

---

## 15. Stripe Payment Method Update Link

**Nombre**  
`POST - Stripe Payment Method Update Link`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/stripe/subscription/payment-method-update-link?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123",
  "returnUrl": "https://<static-website-host>/billing-return.html"
}
```

**Response Object**  
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

---

## 16. Stripe Retry Open Invoice

**Nombre**  
`POST - Stripe Retry Open Invoice`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/stripe/subscription/retry-payment?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123"
}
```

**Response Object**  
```json
{
  "result": true,
  "customerId": "cus_123",
  "subscriptionId": "sub_123",
  "invoiceId": "in_123",
  "invoiceStatus": "paid",
  "collectionMethod": "charge_automatically",
  "invoiceFound": true,
  "paymentAttempted": true,
  "invoicePaid": true,
  "error": null
}
```

---

## 17. Stripe Webhook

**Nombre**  
`POST - Stripe Webhook`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/stripe/webhook?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
Stripe-Signature: <STRIPE_SIGNATURE>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "id": "evt_123",
  "type": "checkout.session.completed",
  "data": {
    "object": {
      "...": "Stripe event payload"
    }
  }
}
```

**Response Object**  
```text
HTTP 200 OK
Empty body on success in the current implementation.
```

Respuesta de error de firma:
```text
HTTP 400 Bad Request
Empty body
```

---

## 18. Cancel Subscription At Period End

**Nombre**  
`POST - Cancel Subscription At Period End`

**Endpoint Url**  
`POST https://<func>.azurewebsites.net/api/stripe/subscription/cancel-at-period-end?code=<FUNCTION_KEY>`

**Headers (If any)**  
```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
x-correlation-id: <optional>
```

**Request Object**  
```json
{
  "scope": "individual",
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "companyId": "C1",
  "subscriptionId": "sub_123"
}
```

**Response Object**  
```json
{
  "ok": true,
  "scope": "individual",
  "subscriptionId": "sub_123",
  "cancelAtPeriodEnd": true,
  "alreadyScheduled": false,
  "effectivePeriodEndUtc": "2026-04-30T00:00:00.0000000Z",
  "currentPeriodEndUtc": "2026-04-30T00:00:00.0000000Z",
  "cancelRequestedAtUtc": "2026-03-31T22:00:00.0000000Z",
  "canceledAtUtc": "",
  "error": null
}
```
