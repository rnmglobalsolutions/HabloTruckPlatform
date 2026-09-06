# Subscription Upgrade And Downgrade Scenarios

This document defines how HabloTruck should handle plan changes after a user or company already has paid access.

It is intentionally separate from the initial checkout docs because changing an existing subscription is different from choosing monthly or yearly before the first payment.

## Scope

This document covers:

- Free to individual paid access.
- Individual monthly to individual yearly.
- Individual yearly to individual monthly.
- Individual access plus company or school seat access.
- Company or school seat to individual paid access.
- Company or fleet seat quantity increases.
- Company or fleet seat quantity decreases.
- What ManyChat should show.
- What Stripe events and backend state should confirm success.

This document does not replace:

- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md`
- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`
- `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md`
- `docs/HabloTruck_Endpoints_Postman_Guide.md`

## Current Implementation Status

### Supported Today

The backend currently supports:

- Initial individual monthly checkout through `POST /api/stripe/payment-link`.
- Initial individual yearly checkout through `POST /api/stripe/payment-link`.
- Initial company/fleet seat checkout through the fleet checkout flow.
- Individual monthly to yearly upgrade through `POST /api/stripe/subscription/change-plan`.
- Individual yearly to monthly downgrade through `POST /api/stripe/subscription/change-plan`.
- Company/fleet seat quantity changes through `POST /api/stripe/subscription/update-seat-quantity`.
- Stripe webhook projection for `checkout.session.completed`, `invoice.paid`, `customer.subscription.updated`, `customer.subscription.deleted`, and payment-failure events.
- Access recomputation after subscription and seat changes.
- ManyChat access sync through tags and custom fields.
- Billing recovery through Stripe Billing Portal payment-method update links.
- Cancel at period end through `POST /api/stripe/subscription/cancel-at-period-end`.

### Not Fully Implemented Yet

The backend does not currently implement:

- Local pending-plan-change records. Stripe subscription schedules are used for individual yearly-to-monthly downgrades.
- Seat decreases below current `SeatsUsed`.
- Automatic ManyChat confirmation messages for successful plan changes.

The current Billing Portal endpoint is specifically a payment-method update flow. Plan and seat changes should use the HabloTruck-owned endpoints above, not the payment-method portal link.

## Key Principle

Stripe owns billing. The HabloTruck backend owns access.

The expected loop is:

1. A plan or quantity change happens in Stripe.
2. Stripe sends webhook events.
3. The backend projects the new subscription facts.
4. The backend recomputes access.
5. The backend queues ManyChat sync.
6. ManyChat receives the updated access tags and fields.

ManyChat should not be the source of truth for access. ManyChat should request changes, show links, and react to backend-confirmed state.

## Access Model

HabloTruck access is computed as:

- `Full`
- `Grace`
- `Blocked`

Access source is computed as:

- `Individual`
- `Company`
- `Both`
- `None`

Important behavior:

- If a user has active individual access and active company seat access, the source should be `Both`.
- If company access ends but individual access remains active, the user should stay `Full` with source `Individual`.
- If individual access ends but company access remains active, the user should stay `Full` with source `Company`.
- If both sources end, grace or blocked rules apply.

ManyChat sync should update:

- `HT_ACCESS_FULL`, `HT_ACCESS_GRACE`, or `HT_ACCESS_BLOCKED`.
- `HT_SRC_INDIVIDUAL` when individual access is active.
- `HT_SRC_COMPANY` when company access is active.
- `ht_access_mode`.
- `ht_grace_ends_utc`.
- `ht_company_id`.

## Scenario Matrix

| Scenario | Recommended Business Rule | Current Backend Reality | ManyChat Action |
| --- | --- | --- | --- |
| Free to monthly | Immediate checkout | Supported | Send `individual_monthly` checkout link |
| Free to yearly | Immediate checkout | Supported | Send `individual_yearly` checkout link |
| Monthly to yearly | Immediate upgrade | Supported by `POST /api/stripe/subscription/change-plan` | Call change-plan with `targetPlanType=individual_yearly` |
| Yearly to monthly | Schedule monthly billing after the paid yearly period ends | Supported by `POST /api/stripe/subscription/change-plan` | Call change-plan with `targetPlanType=individual_monthly`, `effectiveWhen=period_end` |
| Monthly/yearly to company seat | Allow both access sources | Company join/access recompute exists | Let driver claim invite; optionally offer cancel individual plan |
| Company seat to individual | Allow individual checkout | Supported as new individual checkout, if user is linked correctly | Send individual checkout link |
| Add fleet seats | Increase immediately | Supported by `POST /api/stripe/subscription/update-seat-quantity` | Call update-seat-quantity |
| Reduce fleet seats | Safe decrease only when `targetSeats >= SeatsUsed` | Supported by `POST /api/stripe/subscription/update-seat-quantity` | Call update-seat-quantity or route to support |
| Cancel individual | Cancel at period end | Supported | Call cancel-at-period-end endpoint |
| Cancel company subscription | Cancel at period end | Endpoint supports company scope with authorization checks | Use admin/support flow |

## Individual Monthly To Yearly

### Business Rule

This should be treated as an upgrade.

Recommended behavior:

- Apply the yearly plan immediately.
- Let Stripe calculate proration and invoice the difference immediately.
- Require Stripe to complete the immediate payment; if Stripe returns an incomplete-payment error, do not update local plan hints.
- Keep user access as `Full`.
- Update the user plan term to annual after Stripe confirms the subscription update.
- Sync ManyChat state after backend recompute.

### Expected Stripe Events

Expected events may include:

- `customer.subscription.updated`
- `invoice.paid`
- optionally `invoice.payment_failed` if the upgrade payment fails

### Endpoint

```text
POST /api/stripe/subscription/change-plan
```

Request:

```json
{
  "manyChatSubscriberId": "{{manyChatSubscriberId}}",
  "email": "{{email}}",
  "phoneE164": "{{phoneE164}}",
  "targetPlanType": "individual_yearly",
  "effectiveWhen": "immediate"
}
```

### Expected Backend State

After webhook processing:

- `StripePriceId` should be the yearly price ID.
- `PlanType` should be `individual_yearly`.
- `IndividualPlanTerm` should be `annual`.
- `SubscriptionStatus` should be active or paid-through.
- Effective access should be `Full`.
- Access source should include `Individual`.

### ManyChat Copy

Recommended message:

```text
Perfecto. Vamos a cambiar tu acceso al plan anual para que pagues menos durante el ano.
```

After confirmation:

```text
Listo. Tu acceso anual esta activo.
```

If payment fails:

```text
No pudimos completar el cambio al plan anual. Tu acceso actual sigue igual por ahora.
```

## Individual Yearly To Monthly

### Business Rule

This should be treated as a downgrade.

Recommended behavior:

- Do not remove paid yearly value immediately.
- Keep the user paid through the current yearly period.
- Create or update a Stripe subscription schedule so the yearly phase remains active until the paid-through date and the monthly phase starts after.
- Avoid surprise refunds, credits, and confusing partial-period billing unless explicitly handled.
- Do not charge monthly until the annual paid period ends.

### Expected Stripe Events

Expected events may include:

- `customer.subscription.updated`
- future `invoice.paid` when the monthly renewal begins

### Endpoint

```text
POST /api/stripe/subscription/change-plan
```

Request:

```json
{
  "manyChatSubscriberId": "{{manyChatSubscriberId}}",
  "email": "{{email}}",
  "phoneE164": "{{phoneE164}}",
  "targetPlanType": "individual_monthly",
  "effectiveWhen": "period_end"
}
```

Implementation note:

- The endpoint creates or updates a Stripe subscription schedule for yearly to monthly.
- `next_invoice`, `period-end`, and `renewal` are accepted as legacy aliases, but new clients should send `period_end`.
- The paid-through `current_period_end` remains the guardrail that keeps access `Full`.
- Stripe webhooks remain the source of truth after the update.

### Expected Backend State Before Period End

Before the yearly period ends:

- Effective access should remain `Full`.
- The user should not enter grace just because the recurring price changed.
- The local `PlanType` should remain `individual_yearly` until Stripe enters the monthly phase and webhook projection updates the user.
- Current period end should remain the paid-through date.

### Expected Backend State After Period End

After Stripe starts the monthly plan:

- `StripePriceId` should be the monthly price ID.
- `PlanType` should be `individual_monthly`.
- `IndividualPlanTerm` should be `monthly`.
- Effective access should remain `Full` if the new monthly invoice is paid.

### ManyChat Copy

Recommended message:

```text
Tu plan anual ya esta pagado hasta la fecha de renovacion. Podemos programar el cambio para que el plan mensual empiece despues.
```

After confirmation:

```text
Listo. Tu cambio al plan mensual quedo preparado para tu proxima factura, sin cobro extra hoy.
```

## Individual Access Plus Company Seat

### Business Rule

A user can have both individual and company access.

Recommended behavior:

- Do not automatically cancel individual access when the user claims a company seat.
- Compute access source as `Both` while both are active.
- Offer the user a separate option to cancel individual renewal if they no longer need it.

Reason:

Some drivers may want individual access even if a company seat is temporary. Automatically canceling individual access could create accidental churn.

### ManyChat Copy

When a paid individual user claims a company seat:

```text
Listo. Tambien tienes acceso por tu empresa. Tu acceso sigue activo.
```

Optional follow-up:

```text
Si quieres, tambien puedes revisar tu plan individual para decidir si lo mantienes o lo cancelas al final del periodo.
```

## Company Seat To Individual

### Business Rule

If a user loses company access or expects to leave a company, they should be able to buy individual access.

Recommended behavior:

- Let the user buy monthly or yearly individual access.
- If the company seat is still active, access source becomes `Both`.
- If the company seat later expires, access should remain `Full` from the individual source.

### ManyChat Copy

```text
Puedes mantener tu acceso con un plan individual aunque ya no estes usando un seat de empresa.
```

## Fleet Seat Quantity Increase

### Business Rule

Seat increases should usually be immediate.

Recommended behavior:

- Increase Stripe subscription quantity.
- Update the matching company entitlement `SeatsTotal`.
- Keep existing invite code active.
- Allow additional drivers to claim seats.

### Expected Backend State

After Stripe confirms the quantity increase:

- Company entitlement `SeatsTotal` should increase.
- `SeatsUsed` should remain the number of already claimed seats.
- `IsOverCapacity` should be false if `SeatsUsed <= SeatsTotal`.
- Existing users should keep access.
- Stripe must accept the immediate invoice without an incomplete payment state before local capacity is increased.

### Endpoint

```text
POST /api/stripe/subscription/update-seat-quantity
```

Request:

```json
{
  "actorUserPk": "{{backend_user_pk}}",
  "actorUserId": "{{backend_user_id}}",
  "companyId": "{{cf_company_id}}",
  "subscriptionId": "sub_...",
  "targetSeats": 25,
  "effectiveWhen": "immediate"
}
```

### ManyChat Copy

```text
Listo. Agregamos mas seats a tu cuenta. Tus drivers pueden usar el mismo codigo de invitacion.
```

## Fleet Seat Quantity Decrease

### Business Rule

Seat decreases should be handled carefully.

Recommended behavior:

- Do not reduce seats below current `SeatsUsed` without admin review.
- Use no proration for safe decreases.
- If the requested quantity is below active assigned seats, show a support/admin path.

Reason:

Removing seats immediately can accidentally block active drivers.

### Expected Backend State

After a valid decrease:

- `SeatsTotal` should reflect the new quantity.
- If `SeatsUsed > SeatsTotal`, the company should be treated as over capacity and needs admin action.
- Existing active drivers should not be randomly removed without an explicit seat removal policy.

The endpoint rejects unsafe decreases with:

```text
target_below_seats_used
```

### Endpoint

```text
POST /api/stripe/subscription/update-seat-quantity
```

Request:

```json
{
  "actorUserPk": "{{backend_user_pk}}",
  "actorUserId": "{{backend_user_id}}",
  "companyId": "{{cf_company_id}}",
  "subscriptionId": "sub_...",
  "targetSeats": 8,
  "effectiveWhen": "next_invoice"
}
```

### ManyChat Copy

```text
Para bajar la cantidad de seats, primero revisemos cuantos drivers activos tienes para no cortar acceso por error.
```

## Backend Endpoints

### Change Individual Plan

Endpoint:

```text
POST /api/stripe/subscription/change-plan
```

Request:

```json
{
  "manyChatSubscriberId": "{{manyChatSubscriberId}}",
  "email": "{{email}}",
  "phoneE164": "{{phoneE164}}",
  "targetPlanType": "individual_yearly",
  "effectiveWhen": "immediate"
}
```

Recommended `targetPlanType` values:

- `individual_monthly`
- `individual_yearly`

Recommended `effectiveWhen` values:

- `immediate`
- `period_end`

Validation:

- Actor must own the subscription.
- Current Stripe price must be one of the configured individual monthly/yearly prices.
- Target plan must be different from current plan.
- Monthly to yearly can allow `immediate`.
- Yearly to monthly should default to `period_end`.
- Stripe customer ownership must match local user state.

### Update Company Seat Quantity

Endpoint:

```text
POST /api/stripe/subscription/update-seat-quantity
```

Request:

```json
{
  "actorUserPk": "{{backend_user_pk}}",
  "actorUserId": "{{backend_user_id}}",
  "companyId": "{{cf_company_id}}",
  "subscriptionId": "sub_...",
  "targetSeats": 25,
  "effectiveWhen": "immediate"
}
```

Validation:

- Actor must be company admin or authorized operator.
- Target seats must be greater than zero.
- Decrease below `SeatsUsed` should be rejected or escalated.
- Increase must be immediate.
- Decrease must use `next_invoice` billing behavior and no proration.

## ManyChat Routing

Recommended menu options for paid users:

- `Cambiar a plan anual`
- `Cambiar a plan mensual`
- `Cancelar renovacion`
- `Actualizar pago`
- `Hablar con soporte`

Recommended menu options for company admins:

- `Agregar seats`
- `Reducir seats`
- `Ver codigo de invitacion`
- `Cancelar renovacion`
- `Hablar con soporte`

## Observability Checklist

After any upgrade or downgrade, verify:

1. Stripe sent `customer.subscription.updated`.
2. Stripe sent `invoice.paid` if immediate payment was required and succeeded.
3. The webhook request returned `200`.
4. The backend resolved the Stripe customer through `HTUserStripeCustomer`.
5. User or company subscription facts were updated.
6. Access recompute ran.
7. `manychat.dispatch.queued` recorded `actionType=manychat_sync`.
8. `manychat.dispatch.processed` recorded `outcome=completed`.
9. ManyChat tags and fields reflect the new access state.

Useful queries:

- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 6I. Stripe Event Types Received`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 6N. Drill Into One Stripe Webhook Operation`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 6Q. Classify Stripe Webhooks By Storage Activity`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 6B. Did Access Sync Get Queued And Processed`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 20. Subscription Plan Change Outcomes`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 21. Company Seat Quantity Change Outcomes`

## Current Product Decision Needed

Remaining product decisions:

- Should monthly to yearly charge immediately with proration, or switch at renewal?
- What happens when a company reduces seats below active drivers?
- Should ManyChat send users to Stripe Billing Portal for plan management, or should HabloTruck own plan-change endpoints?

Recommended default:

- Monthly to yearly: immediate upgrade.
- Yearly to monthly: period-end scheduled downgrade.
- Add seats: immediate.
- Reduce seats: no proration, and only if target seats are not below active seats.
- Use HabloTruck-owned endpoints for plan and quantity changes so access, telemetry, and ManyChat sync remain predictable.
