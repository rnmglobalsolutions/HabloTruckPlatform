# ManyChat Company and Invite Flows

This document describes the end-to-end ManyChat flow for HabloTruck company and school journeys:

1. Company or school admin purchases seats.
2. Admin retrieves or resends the active invite code.
3. Driver or student joins using the invite code.

It covers:

- Happy path.
- Error variants.
- Additional recommended scenarios.
- An operational ManyChat blueprint with blocks, exact copy, logic, and backend calls.

## Scope

This document only covers:

- `company/fleet/start-checkout`
- `company/invite/active`
- `company/invite/resend`
- `company/invite/info`
- `company/join`

It does not cover:

- individual monthly
- individual yearly

## Key Backend Reality

The current backend already supports the full company-seat lifecycle.

Operationally:

1. ManyChat starts a fleet checkout.
2. Stripe checkout is completed by the admin.
3. Stripe webhook creates or updates the `Company`.
4. Stripe webhook creates or updates the `Entitlement`.
5. Stripe webhook auto-creates an active invite code for that entitlement.
6. ManyChat can retrieve that active code.
7. Drivers or students validate the code.
8. Drivers or students join through `company/join`.
9. The backend consumes invite usage, assigns a seat, recalculates access, and asynchronously syncs access state back to ManyChat.

Important:

- The invite is auto-created after successful fleet checkout.
- The backend supports showing the current active invite and resending it.
- ManyChat should still include a user-facing verification and fallback experience.

## Relevant Endpoints

- `POST /api/company/fleet/start-checkout`
- `POST /api/company/invite/active`
- `POST /api/company/invite/resend`
- `POST /api/company/invite/info`
- `POST /api/company/join`

Related backend files:

- `HabloTruckPlatform/Functions/StartFleetCheckoutFunction.cs`
- `HabloTruckPlatform/Functions/GetActiveInviteFunction.cs`
- `HabloTruckPlatform/Functions/ResendAdminInviteFunction.cs`
- `HabloTruckPlatform/Functions/GetInviteInfoFunction.cs`
- `HabloTruckPlatform/Functions/JoinCompanyFunction.cs`
- `HabloTruckPlatform.Application/UseCases/StripeSubscriptionHandler.cs`
- `HabloTruckPlatform.Application/UseCases/CompanyJoinHandler.cs`

## Request Contracts

## Start Fleet Checkout

```json
{
  "companyName": "{{cf_company_name}}",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "seats": {{cf_requested_seats}},
  "successUrl": "https://tu-frontend.com/company-success",
  "cancelUrl": "https://tu-frontend.com/company-cancel"
}
```

## Get Active Invite

```json
{
  "companyId": "{{cf_company_id}}"
}
```

## Resend Active Invite

```json
{
  "companyId": "{{cf_company_id}}",
  "entitlementId": "{{cf_entitlement_id}}"
}
```

## Validate Invite Code

```json
{
  "inviteCode": "{{cf_invite_code}}"
}
```

## Join Company

```json
{
  "inviteCode": "{{cf_invite_code}}",
  "email": "{{contact.email}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "phoneE164": "{{cf_phone_e164}}"
}
```

## Headers

```text
Content-Type: application/json
x-api-key: <HttpApiKey>
```

## Response Contracts

## Start Fleet Checkout Success

```json
{
  "ok": true,
  "error": null,
  "companyId": "C_01JQ...",
  "companyName": "Acme Trucking",
  "seats": 20,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_..."
}
```

## Active Invite Success

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
  "createdAtUtc": "2026-04-01T00:00:00.0000000Z",
  "expiresAtUtc": ""
}
```

## Invite Validation Success

```json
{
  "ok": true,
  "valid": true,
  "code": "HT-AB12CD",
  "status": "active",
  "companyId": "C1",
  "entitlementId": "E1",
  "expiresAtUtc": "",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16
}
```

## Join Company Success

```json
{
  "ok": true,
  "alreadyJoined": false,
  "companyId": "C1",
  "entitlementId": "E1"
}
```

## ManyChat State Recommended

Recommended custom fields:

- `cf_company_name`
- `cf_requested_seats`
- `cf_company_id`
- `cf_entitlement_id`
- `cf_invite_code`
- `cf_phone_e164`
- `cf_last_company_error`

Recommended synchronized tags or fields:

- `HT_ACCESS_FULL`
- `HT_SRC_COMPANY`
- custom field `ht_company_id`
- custom field `ht_access_mode`

## 1. Happy Path

## 1.1 Admin buys a company or school package

### Step 1: Admin chooses company path

ManyChat presents:

- `Empresa o escuela`

### Step 2: ManyChat collects company purchase data

ManyChat should collect:

- company name
- requested seats
- email
- optional phone
- `{{contact.id}}` as `manyChatSubscriberId`
- optional `manyChatChannel` such as `facebook`, `instagram`, or `whatsapp`

### Step 3: ManyChat requests fleet checkout

ManyChat calls:

```text
POST /api/company/fleet/start-checkout
```

If successful:

- backend returns `ok = true`
- ManyChat receives `companyId`
- ManyChat receives `url`
- ManyChat stores `cf_company_id`
- ManyChat sends admin to Stripe

### Step 4: Admin completes checkout in Stripe

The company or school purchase is completed in Stripe.

### Step 5: Stripe notifies backend by webhook

The backend then:

- creates or updates the company
- creates or updates the entitlement
- auto-creates an active invite code for that entitlement

### Step 6: ManyChat retrieves the invite

After payment, ManyChat should call:

```text
POST /api/company/invite/active
```

using `cf_company_id`.

### Step 7: ManyChat shows the invite to the admin

If successful:

- show the code
- explain that the code must be shared with drivers or students

## 1.2 Driver or student joins with code

### Step 1: User chooses invite-code path

ManyChat presents:

- `Ya tengo código`

### Step 2: ManyChat asks for invite code

The user submits `cf_invite_code`.

### Step 3: ManyChat validates the code

ManyChat calls:

```text
POST /api/company/invite/info
```

If valid:

- continue

If invalid:

- show error and offer retry

### Step 4: ManyChat collects user identity if needed

ManyChat should send:

- email
- optional phone
- `manyChatSubscriberId`
- optional `manyChatChannel`

### Step 5: ManyChat requests join

ManyChat calls:

```text
POST /api/company/join
```

### Step 6: Backend applies join

The backend then:

- resolves or creates the user
- checks whether user is already in this company
- blocks if user belongs to another company
- consumes invite usage
- assigns or confirms seat
- recalculates access
- queues ManyChat access synchronization

### Step 7: ManyChat verifies access and closes the loop

ManyChat should verify:

- tag `HT_ACCESS_FULL`
- `HT_SRC_COMPANY`
- `ht_company_id`

Then show success and route user into HabloTruck full-access experience.

## 2. Error Variants

## 2.1 Communication failure between ManyChat and backend

This can happen for:

- fleet checkout creation
- active invite lookup
- resend invite
- invite validation
- join company

Recommended ManyChat response:

```text
No pude completar esta acción en este momento.

Puede ser un problema temporal de conexión.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

## 2.2 Fleet checkout creation fails before Stripe

Examples:

- invalid seat count
- missing company name
- missing success/cancel URLs
- Stripe checkout session creation failure

Recommended ManyChat response:

```text
No pude preparar el pago para tu empresa o escuela.

Vamos a intentarlo otra vez.
```

Buttons:

- `Intentar otra vez`
- `Volver`
- `Hablar con soporte`

## 2.3 Admin pays but invite is not shown immediately

Possible reasons:

- webhook still processing
- checkout succeeded but `invite/active` is called too early
- there is a temporary delay between purchase projection and invite retrieval

Recommended ManyChat response:

```text
Tu compra ya puede estar en proceso, pero todavía no veo tu código listo.

Voy a intentarlo otra vez en un momento.
```

Buttons:

- `Volver a buscar mi código`
- `Hablar con soporte`

## 2.4 No active invite found

This may mean:

- webhook not finished yet
- entitlement not found
- company ID mismatch

Recommended ManyChat response:

```text
Todavía no encontré un código activo para tu empresa.
```

Buttons:

- `Volver a buscar`
- `Reenviar código`
- `Hablar con soporte`

## 2.5 Invite code invalid, expired, or full

If `invite/info` returns `valid = false`, show:

```text
Ese código no es válido, ya venció o ya no tiene cupos disponibles.
```

Buttons:

- `Escribir otro código`
- `Hablar con soporte`

## 2.6 User is already in another company

If `company/join` returns conflict because the user belongs to a different company:

```text
Tu cuenta ya está asociada a otra empresa.
```

Buttons:

- `Hablar con soporte`

## 2.7 User already joined this same company

If `company/join` returns:

```json
{
  "ok": true,
  "alreadyJoined": true
}
```

ManyChat should treat this as success, not as an error.

Recommended response:

```text
Tu acceso con esta empresa ya estaba activo.
```

Buttons:

- `Entrar ahora`

## 2.8 Seat assignment conflict or no seats available

These are important operationally:

- `no_seats_available`
- `company_over_capacity`
- `seat_assignment_conflict`
- `seat_reservation_conflict`

Recommended ManyChat response:

```text
No pude completar tu acceso con este código en este momento.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

If the problem persists, support should review the entitlement and invite state.

## 2.9 Admin loses the invite code

This is not really a system failure, but it is a common user journey.

ManyChat should call:

```text
POST /api/company/invite/resend
```

and show the returned code again.

## 3. Other Recommended Scenarios

## 3.1 "Ya pagué" for admin after checkout

Admins need a post-checkout verification path just like individual buyers.

Recommended button:

- `Ya pagué`

That should trigger:

- `invite/active`

until the code is found.

## 3.2 "Ver mi código otra vez"

Admins often come back later and need the code again.

Recommended keyword or button:

- `Mi código`
- `Ver mi código`
- `Reenviar código`

These should go to the active-invite or resend flow.

## 3.3 Prevent duplicate seat purchase from confusion

Before creating a new company checkout, ManyChat should ask:

```text
¿Quieres comprar un nuevo paquete o solo ver tu código actual?
```

Buttons:

- `Comprar seats`
- `Ver mi código`

## 3.4 Driver paid individually but also has company code

This is a product and policy decision, but ManyChat should avoid silently overlapping paths.

Recommended message:

```text
Ya tienes acceso activo. Si también tienes código de empresa, te ayudo a revisarlo antes de continuar.
```

## 4. Operational ManyChat Blueprint

This section describes the recommended ManyChat build using flows, blocks, exact copy, logic, and endpoint calls.

## Flow 1 - Comprar seats para empresa o escuela

### Goal

Collect admin data and start a fleet checkout.

### Block 1.1

Type:

- `Send Message`

Text:

```text
Si tienes una empresa o escuela, puedes activar acceso para tu equipo.
```

Button:

- `Continuar`

### Block 1.2

Type:

- `User Input`

Prompt:

```text
¿Cómo se llama tu empresa o escuela?
```

Save to:

- `cf_company_name`

### Block 1.3

Type:

- `User Input`

Prompt:

```text
¿Cuántos accesos necesitas?
```

Save to:

- `cf_requested_seats`

### Block 1.4

Type:

- `User Input`

Prompt:

```text
Confírmame tu email para enviarte y activar todo correctamente.
```

Save to:

- ManyChat email or `cf_email`

### Block 1.5

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/company/fleet/start-checkout
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "companyName": "{{cf_company_name}}",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "seats": {{cf_requested_seats}},
  "successUrl": "https://tu-frontend.com/company-success",
  "cancelUrl": "https://tu-frontend.com/company-cancel"
}
```

Recommended saves:

- `cf_company_id = response.companyId`
- `cf_last_company_error = response.error`

### Block 1.6

Type:

- `Condition`

Logic:

- if `response.ok == true`
- and `response.url` is present

If true:

- go to `Flow 2 - Enviar admin a Stripe`

If false:

- go to `Flow 7 - Error al crear checkout de empresa`

## Flow 2 - Enviar admin a Stripe

### Goal

Send admin to checkout and explain the next step.

### Block 2.1

Type:

- `Send Message`

Text:

```text
Perfecto. Tu enlace de pago ya está listo.

Cuando termines el pago, regresa aquí para obtener el código que vas a compartir con tu equipo.
```

Button:

- `Ir al pago`

Action:

- open website `{{response.url}}`

### Block 2.2

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

- `Ya pagué` -> `Flow 3 - Buscar código activo`
- `Tuve un problema` -> `Flow 8 - Pago cancelado o sin código`

## Flow 3 - Buscar código activo

### Goal

Retrieve the active invite generated after fleet checkout.

### Block 3.1

Type:

- `Send Message`

Text:

```text
Estoy buscando el código activo de tu empresa.
```

### Block 3.2

Type:

- `Smart Delay`

Suggested delay:

- 10 to 20 seconds

### Block 3.3

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/company/invite/active
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "companyId": "{{cf_company_id}}"
}
```

Recommended saves:

- `cf_entitlement_id = response.entitlementId`
- `cf_invite_code = response.code`

### Block 3.4

Type:

- `Condition`

Logic:

- if `response.ok == true`
- and `response.code` exists

If true:

- go to `Flow 4 - Mostrar código al admin`

If false:

- go to `Flow 9 - Código no encontrado todavía`

## Flow 4 - Mostrar código al admin

### Goal

Show the invite code to the admin.

### Block 4.1

Type:

- `Send Message`

Text:

```text
Tu código de acceso para tu equipo es:

{{cf_invite_code}}
```

### Block 4.2

Type:

- `Send Message`

Text:

```text
Compártelo con tus choferes o alumnos para que puedan activar su acceso.
```

Buttons:

- `Ver mi código otra vez`
- `Cómo lo usan`

Actions:

- `Ver mi código otra vez` -> `Flow 5 - Reenviar código`
- `Cómo lo usan` -> `Flow 6 - Unirme con código`

## Flow 5 - Reenviar código

### Goal

Return the active code again or recreate it safely if needed.

### Block 5.1

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/company/invite/resend
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "companyId": "{{cf_company_id}}",
  "entitlementId": "{{cf_entitlement_id}}"
}
```

### Block 5.2

Type:

- `Condition`

Logic:

- if `response.ok == true`
- and `response.code` exists

If true:

- save `cf_invite_code = response.code`
- return to `Flow 4 - Mostrar código al admin`

If false:

- show support fallback

## Flow 6 - Unirme con código

### Goal

Allow drivers or students to join with a company code.

### Block 6.1

Type:

- `Send Message`

Text:

```text
Si ya tienes un código de empresa o escuela, escríbelo aquí.
```

### Block 6.2

Type:

- `User Input`

Prompt:

```text
Escribe tu código.
```

Save to:

- `cf_invite_code`

### Block 6.3

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/company/invite/info
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "inviteCode": "{{cf_invite_code}}"
}
```

### Block 6.4

Type:

- `Condition`

Logic:

- if `response.ok == true`
- and `response.valid == true`

If true:

- save `cf_company_id = response.companyId`
- save `cf_entitlement_id = response.entitlementId`
- go to `Flow 10 - Completar join`

If false:

- go to `Flow 11 - Código inválido`

## Flow 7 - Error al crear checkout de empresa

### Goal

Handle failures before Stripe checkout is created.

### Block 7.1

Type:

- `Send Message`

Text:

```text
No pude preparar el pago para tu empresa o escuela en este momento.
```

Buttons:

- `Intentar otra vez`
- `Volver`
- `Hablar con soporte`

## Flow 8 - Pago cancelado o sin código

### Goal

Handle cancellation, abandonment, or admin uncertainty.

### Block 8.1

Type:

- `Send Message`

Text:

```text
Si no pudiste terminar el pago, no te preocupes.

Puedes intentarlo otra vez cuando quieras.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

## Flow 9 - Código no encontrado todavía

### Goal

Handle the case where the admin paid but the active code is not found yet.

### Block 9.1

Type:

- `Send Message`

Text:

```text
Todavía no encontré el código activo de tu empresa.

Es posible que el pago todavía se esté procesando.
```

Buttons:

- `Volver a buscar mi código`
- `Reenviar código`
- `Hablar con soporte`

Actions:

- `Volver a buscar mi código` -> `Flow 3`
- `Reenviar código` -> `Flow 5`

## Flow 10 - Completar join

### Goal

Join the user into the company entitlement.

### Block 10.1

Type:

- `External Request`

Method:

- `POST`

URL:

```text
https://TU_FUNCTION_APP.azurewebsites.net/api/company/join
```

Headers:

```text
Content-Type: application/json
x-api-key: TU_HTTP_API_KEY
```

Body:

```json
{
  "inviteCode": "{{cf_invite_code}}",
  "email": "{{contact.email}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "phoneE164": "{{cf_phone_e164}}"
}
```

### Block 10.2

Type:

- `Condition`

Logic:

- if `response.ok == true`

If true:

- go to `Flow 12 - Acceso de empresa activado`

If false:

- go to `Flow 13 - Error al unirse`

## Flow 11 - Código inválido

### Goal

Handle invalid, expired, or exhausted invite codes.

### Block 11.1

Type:

- `Send Message`

Text:

```text
Ese código no es válido, ya venció o ya no tiene cupos disponibles.
```

Buttons:

- `Escribir otro código`
- `Hablar con soporte`

## Flow 12 - Acceso de empresa activado

### Goal

Confirm the user successfully joined through the company path.

### Block 12.1

Type:

- `Send Message`

Text:

```text
Listo. Tu acceso con tu empresa o escuela ya está activo.
```

Buttons:

- `Empezar ahora`
- `Ver temas`
- `Escuchar frases`

## Flow 13 - Error al unirse

### Goal

Handle join-company conflicts or operational failures.

### Block 13.1

Type:

- `Send Message`

Text:

```text
No pude completar tu acceso con este código en este momento.
```

Buttons:

- `Intentar otra vez`
- `Hablar con soporte`

Actions:

- `Intentar otra vez` -> `Flow 10`

## Recommended Minimum Flow Set

For company and invite journeys, the minimum ManyChat setup should include:

1. `Comprar seats para empresa o escuela`
2. `Enviar admin a Stripe`
3. `Buscar código activo`
4. `Mostrar código al admin`
5. `Reenviar código`
6. `Unirme con código`
7. `Error al crear checkout de empresa`
8. `Pago cancelado o sin código`
9. `Código no encontrado todavía`
10. `Completar join`
11. `Código inválido`
12. `Acceso de empresa activado`
13. `Error al unirse`

## Key Operational Conclusion

With the current backend, the correct operational experience is:

1. ManyChat starts fleet checkout.
2. Admin pays in Stripe.
3. Stripe webhook creates company, entitlement, and active invite.
4. ManyChat retrieves the invite code.
5. Driver or student validates code.
6. ManyChat calls `company/join`.
7. Backend syncs access back to ManyChat.
8. ManyChat shows final success.

That is the most reliable current design for company, school, and invite-code flows.
