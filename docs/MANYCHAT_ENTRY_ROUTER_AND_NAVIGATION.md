# ManyChat Entry Router and Main Navigation

This document defines the main conversational architecture for HabloTruck in ManyChat:

1. The entry router.
2. The main menu.
3. The navigation rules.
4. How users should be routed depending on access state and intent.

It is the document that sits above the specific journey documents.

## Purpose

The goal of the entry router is to make sure the user reaches the correct journey quickly, without confusion or overload.

For HabloTruck, the first routing decision should separate these user types:

- new individual buyer
- company or school admin
- driver or student with invite code
- current paid subscriber
- user in billing recovery or renewal state

## Recommended Entry Points

The bot should support these entry points:

- welcome message
- keyword `info`
- keyword `inicio`
- keyword `menu`
- keyword `ya pagué`
- keyword `mi código`
- keyword `actualizar pago`
- keyword `mi acceso`

## Flow 0 - Entry Router

### Goal

Classify the user into the right path immediately.

### Block 0.1

Type:

- `Send Message`

Text:

```text
Bienvenido a HabloTruck.

Aquí vas a aprender el inglés real que se usa en la carretera en Estados Unidos.

¿Cómo quieres comenzar?
```

Buttons:

- `Quiero acceso completo`
- `Soy empresa o escuela`
- `Ya tengo código`

Actions:

- `Quiero acceso completo` -> `Flow 1 - Router de acceso individual`
- `Soy empresa o escuela` -> `Flow 2 - Router empresa o escuela`
- `Ya tengo código` -> `Flow 3 - Router invite code`

## Flow 1 - Router de acceso individual

### Goal

Decide si el usuario necesita comprar, verificar acceso o resolver un problema de pago.

### Block 1.1

Type:

- `Condition`

Suggested checks:

- if tag `HT_ACCESS_FULL`
- if tag `HT_BILLING_ACTION_REQUIRED`
- if `ht_billing_recovery_status` is present

### Routing logic

If user has `HT_ACCESS_FULL`:

- go to `Flow 4 - Menú principal con acceso`

If user has active billing recovery state:

- go to `Flow 5 - Menú de facturación`

If user has no active access:

- go to `Flow 6 - Elegir plan individual`

## Flow 2 - Router empresa o escuela

### Goal

Decide if the person wants to buy seats or retrieve an existing invite code.

### Block 2.1

Type:

- `Send Message`

Text:

```text
Si tienes una empresa o escuela, puedo ayudarte a activar acceso para tu equipo o mostrarte tu código actual.
```

Buttons:

- `Comprar seats`
- `Ver mi código`

Actions:

- `Comprar seats` -> `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md / Flow 1 - Comprar seats para empresa o escuela`
- `Ver mi código` -> `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md / Flow 3 - Buscar código activo`

## Flow 3 - Router invite code

### Goal

Send drivers or students directly into invite validation and join.

### Block 3.1

Type:

- `Send Message`

Text:

```text
Perfecto. Si ya tienes un código de empresa o escuela, te ayudo a activarlo ahora mismo.
```

Button:

- `Continuar`

Action:

- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md / Flow 6 - Unirme con código`

## Flow 4 - Menú principal con acceso

### Goal

Give already-active users a useful menu instead of selling again.

### Block 4.1

Type:

- `Send Message`

Text:

```text
Tu acceso a HabloTruck ya está activo.

¿Qué quieres hacer ahora?
```

Buttons:

- `Empezar ahora`
- `Ver temas`
- `Mi cuenta`

Actions:

- `Empezar ahora` -> learning flow
- `Ver temas` -> catalog flow
- `Mi cuenta` -> `Flow 7 - Mi cuenta`

## Flow 5 - Menú de facturación

### Goal

Route users who have billing friction to the correct recovery flow.

### Block 5.1

Type:

- `Send Message`

Text:

```text
Veo que tu cuenta necesita atención de pago.

Te ayudo a resolverlo.
```

Buttons:

- `Actualizar método de pago`
- `Intentar cobro otra vez`
- `Ver mi estado`

Actions:

- `Actualizar método de pago` -> `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md / Flow 2 - Crear link de actualización`
- `Intentar cobro otra vez` -> `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md / Flow 4 - Reintentar cobro`
- `Ver mi estado` -> `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md / Flow 5 - Verificar estado de recuperación`

## Flow 6 - Elegir plan individual

### Goal

Send non-paying users into the individual subscription flow.

### Action

Route to:

- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md / Flow 1 - Elegir plan individual`

## Flow 7 - Mi cuenta

### Goal

Provide a simple account center for the user.

### Block 7.1

Type:

- `Send Message`

Text:

```text
Desde aquí puedes revisar tu acceso y resolver temas de tu cuenta.
```

Buttons:

- `Ver mi acceso`
- `Actualizar pago`
- `Volver al inicio`

Actions:

- `Ver mi acceso` -> check tags/fields and summarize state
- `Actualizar pago` -> billing recovery or billing portal flow
- `Volver al inicio` -> `Flow 0 - Entry Router`

## Suggested Navigation Rules

These rules keep the bot simple and stable:

- If the user already has `HT_ACCESS_FULL`, do not immediately sell again.
- If the user is in billing recovery, route to billing resolution before trying to resell.
- If the user has company access, route to content, not to individual purchase by default.
- If the user says `ya pagué`, route to verification instead of restarting the journey.
- If the user says `mi código`, route to active-invite or resend flow.

## Keywords Recommended

### Access and checkout

- `info`
- `inicio`
- `menu`
- `ya pagué`
- `mi acceso`

### Company

- `mi código`
- `código`
- `empresa`
- `escuela`

### Billing

- `pago`
- `actualizar pago`
- `actualizar tarjeta`
- `billing`

## Document Connections

This router should connect to:

- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md`
- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`
- `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md`

## Key Operational Conclusion

The main router should not try to explain every feature.

Its job is only to classify the user fast and safely into the right path:

- buy individual
- buy for company
- join with code
- verify access
- fix billing
