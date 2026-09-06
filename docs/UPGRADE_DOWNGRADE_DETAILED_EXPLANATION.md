# HabloTruck Upgrade And Downgrade Detailed Explanation

This document explains every HabloTruck upgrade and downgrade path in business, billing, access, ManyChat, and observability terms.

It is written for product, support, ManyChat flow building, QA, and backend troubleshooting.

## Golden Rules

1. Stripe owns billing.
2. HabloTruck backend owns access.
3. ManyChat should never be the source of truth for access.
4. Immediate paid upgrades must only be treated as successful if Stripe accepts the update without an incomplete payment state.
5. Downgrades must not accidentally remove already-paid value.
6. Company seat reductions must not cut off active drivers by mistake.
7. Webhooks remain the long-term source of truth even when an endpoint returns success.

## Main Terms

`immediate` means the change is applied now.

For paid upgrades, `immediate` also means Stripe may generate an invoice right away. HabloTruck sends `payment_behavior=error_if_incomplete` for those updates so the backend does not mark the upgrade as successful if Stripe cannot collect payment.

`period_end` means the individual yearly-to-monthly downgrade is scheduled for the end of the already-paid yearly period. The backend creates or updates a Stripe subscription schedule so the current yearly phase stays active until the paid-through date and the monthly phase starts after that.

`next_invoice` means a company seat decrease is applied with no proration and billing impact is expected on the next invoice. This is used for seat quantity decreases, not for individual yearly-to-monthly downgrades.

For individual plan changes, `next_invoice`, `period-end`, and `renewal` are accepted only as legacy aliases for `period_end`. New clients should send `period_end`.

`Full` means the user has access.

`Grace` means the user is temporarily allowed after a payment or billing issue.

`Blocked` means the user should not receive paid content.

## Quick Matrix

| Flow | Direction | Endpoint | Effective When | Stripe Proration | Payment Behavior | Access Result |
| --- | --- | --- | --- | --- | --- | --- |
| Free to monthly | Upgrade | `POST /api/stripe/payment-link` | immediate checkout | Stripe checkout | Stripe checkout | `Full` after paid webhook |
| Free to yearly | Upgrade | `POST /api/stripe/payment-link` | immediate checkout | Stripe checkout | Stripe checkout | `Full` after paid webhook |
| Monthly to yearly | Upgrade | `POST /api/stripe/subscription/change-plan` | `immediate` | `always_invoice` | `error_if_incomplete` | Stays `Full` |
| Yearly to monthly | Downgrade | `POST /api/stripe/subscription/change-plan` | `period_end` | schedule next phase | default Stripe behavior | Stays `Full` until annual period ends |
| Add company seats | Upgrade | `POST /api/stripe/subscription/update-seat-quantity` | `immediate` | `always_invoice` | `error_if_incomplete` | More seat capacity after Stripe succeeds |
| Reduce company seats | Downgrade | `POST /api/stripe/subscription/update-seat-quantity` | `next_invoice` | `none` | default Stripe behavior | Capacity reduced only if safe |
| Cancel renewal | Downgrade-like churn control | `POST /api/stripe/subscription/cancel-at-period-end` | period end cancel | no immediate change | default Stripe behavior | Stays `Full` until paid-through date |

## 1. Free To Individual Monthly

This is an upgrade because the user moves from no paid access to paid individual access.

The user starts in free mode or limited access. ManyChat sends the user to the monthly checkout flow. The backend creates a Stripe checkout/payment link for `individual_monthly`.

The payment is not considered complete just because the link was created. Access should become `Full` only after Stripe confirms payment through webhook events such as `checkout.session.completed` and/or `invoice.paid`.

Backend behavior:

- Creates the Stripe checkout/payment link.
- Waits for Stripe webhook confirmation.
- Projects the user subscription facts.
- Recomputes access.
- Queues ManyChat sync.

ManyChat behavior:

- Send the monthly checkout link.
- Do not manually grant paid tags before backend confirmation.
- After sync, user should receive `HT_ACCESS_FULL` and `HT_SRC_INDIVIDUAL`.

Failure behavior:

- If payment is abandoned or fails, user stays free/limited.
- ManyChat should continue showing the upgrade path.

## 2. Free To Individual Yearly

This is also an upgrade from no paid access to paid individual access, but with yearly billing.

The flow is almost the same as free to monthly. The only difference is the target plan and Stripe price.

Backend behavior:

- Creates checkout/payment link for `individual_yearly`.
- Waits for Stripe webhook confirmation.
- Stores yearly subscription facts after Stripe confirms.
- Recomputes access.
- Queues ManyChat sync.

Access result:

- User becomes `Full` after successful payment confirmation.
- Access source becomes `Individual`.
- Plan term should be annual/yearly.

ManyChat behavior:

- Position yearly as the better-value plan.
- Do not grant paid access until backend access sync confirms it.

## 3. Individual Monthly To Individual Yearly

This is the main individual upgrade flow.

The user already has paid monthly access and wants to move to the yearly plan. This should be immediate because the yearly plan is a higher-value commitment and the user should receive annual billing benefits right away.

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

Stripe behavior:

- The backend updates the subscription price to the yearly price.
- It sends `proration_behavior=always_invoice`.
- It sends `billing_cycle_anchor=now`.
- It sends `payment_behavior=error_if_incomplete`.

Why `payment_behavior=error_if_incomplete` matters:

If Stripe cannot collect the immediate upgrade invoice, the endpoint fails. That prevents HabloTruck from marking the user as yearly when payment did not fully go through.

Backend behavior:

- Validates actor user.
- Resolves subscription ID from request or local user.
- Reads current Stripe subscription.
- Verifies the Stripe customer matches the local user.
- Maps `individual_yearly` to the configured yearly Stripe price.
- Calls Stripe.
- Updates local user subscription hints only after Stripe accepts the update.
- Emits App Insights logs and metric `subscription.plan_change`.

Access result:

- User remains `Full`.
- Access source remains `Individual`.
- `PlanType` becomes `individual_yearly`.
- `IndividualPlanTerm` becomes `annual`.

Possible webhook events:

- `customer.subscription.updated`
- `invoice.paid`
- `invoice.payment_failed` if payment fails after another billing event

ManyChat should say:

```text
Listo. Cambiamos tu acceso al plan anual.
```

If Stripe cannot complete payment:

```text
No pudimos completar el cambio al plan anual. Tu plan actual sigue igual por ahora.
```

What to verify in App Insights:

- `subscription.plan_change` with `outcome=completed`
- `reason=individual_upgrade_applied`
- `targetPlanType=individual_yearly`
- `effectiveWhen=immediate`

## 4. Individual Yearly To Individual Monthly

This is the main individual downgrade flow.

The user already paid for yearly access. The business rule is that HabloTruck should not take away paid value immediately and should not create confusing refunds or credits unless explicitly handled.

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
  "targetPlanType": "individual_monthly",
  "effectiveWhen": "period_end"
}
```

Stripe behavior:

- The backend creates or updates a Stripe subscription schedule.
- Phase 1 keeps the current yearly price until the current paid-through period ends.
- Phase 2 starts at the paid-through date with the monthly price.
- There is no immediate refund, credit, or monthly charge.
- The first monthly bill starts when the yearly paid period ends.

Example:

If the user paid `97 USD + taxes` for the yearly plan and downgrades during month 3, the remaining 9 months stay paid. HabloTruck should not charge monthly during those 9 months. The first monthly bill should start when the annual paid period ends, at `12.99 USD + taxes` per month, unless pricing changes before renewal.

Backend behavior:

- Validates actor user and subscription ownership.
- Reads current Stripe subscription.
- Verifies customer ownership.
- Maps `individual_monthly` to the configured monthly Stripe price.
- Calls Stripe to create/update the subscription schedule.
- Stores current subscription hints after Stripe accepts the schedule.
- Does not switch the local user plan to monthly immediately.
- Emits App Insights logs and metric `subscription.plan_change`.

Access result:

- User remains `Full` while paid through.
- `StripeCurrentPeriodEndUtc` protects the already-paid yearly value.
- `PlanType` should remain `individual_yearly` until Stripe enters the monthly phase and webhook projection updates the user.
- The user should not enter grace or blocked just because they downgraded.

ManyChat should say:

```text
Listo. Tu plan anual sigue activo hasta que termine el periodo que ya pagaste. Despues de esa fecha, tu plan mensual empezara en $12.99 mas taxes al mes.
```

What to verify in App Insights:

- `subscription.plan_change` with `outcome=completed`
- `reason=individual_downgrade_scheduled_period_end`
- `targetPlanType=individual_monthly`
- `effectiveWhen=period_end`

## How ManyChat Identifies The User For Plan Changes

ManyChat usually does not know HabloTruck internal fields like `actorUserPk`, `actorUserId`, or `subscriptionId`.

That is expected.

For individual upgrade/downgrade calls, ManyChat should pass the identity fields it already has:

```json
{
  "manyChatSubscriberId": "{{manyChatSubscriberId}}",
  "email": "{{email}}",
  "phoneE164": "{{phoneE164}}",
  "targetPlanType": "individual_yearly",
  "effectiveWhen": "immediate"
}
```

The backend resolves the user in this order:

1. Existing `actorUserPk` + `actorUserId`, if provided.
2. `manyChatSubscriberId`.
3. normalized `email` / `emailNormalized`.
4. `phoneE164`.

After resolving the user, the backend uses the user's stored `StripeSubscriptionId` if the request does not include `subscriptionId`.

The response returns:

```json
{
  "actorUserPk": "U_...",
  "actorUserId": "USER_...",
  "subscriptionId": "sub_...",
  "scheduledChangeEffectiveAtUtc": "2027-01-01T00:00:00.0000000Z"
}
```

ManyChat should store those response values in custom fields when available. Future calls can then use either the internal IDs or the public identity fields.

If the resolved subscription is not an individual monthly/yearly subscription, the endpoint returns:

```text
current_subscription_not_individual
```

That means the user is likely on company-seat access or the wrong subscription was passed. Send that user to individual checkout or the company admin seat flow instead of trying to change an individual plan.

## 5. Add Company/Fleet Seats

This is the main company upgrade flow.

The company already has a fleet subscription and needs more driver seats. This should be immediate because the company wants to invite or activate more drivers right away.

Endpoint:

```text
POST /api/stripe/subscription/update-seat-quantity
```

Request:

```json
{
  "actorUserPk": "{{actorUserPk}}",
  "actorUserId": "{{actorUserId}}",
  "companyId": "{{companyId}}",
  "subscriptionId": "{{subscriptionId}}",
  "targetSeats": 25,
  "effectiveWhen": "immediate"
}
```

Stripe behavior:

- The backend updates the subscription item quantity.
- It sends `proration_behavior=always_invoice`.
- It sends `payment_behavior=error_if_incomplete`.

Why this matters:

If the company card cannot pay the immediate invoice, HabloTruck should not increase local seat capacity. Otherwise, the company could invite more drivers without the billing upgrade being completed.

Backend behavior:

- Validates actor user.
- Validates company exists.
- Verifies actor belongs to the company or matches the admin email.
- Loads company entitlement `ent_<subscriptionId>`.
- Reads Stripe subscription.
- Verifies Stripe customer matches company Stripe customer when available.
- Updates Stripe quantity.
- Updates local entitlement `SeatsTotal` only after Stripe succeeds.
- Emits App Insights logs and metric `company.seat_quantity_change`.

Access result:

- Existing drivers stay active.
- `SeatsTotal` increases.
- More drivers can claim seats using the existing invite flow.
- `IsOverCapacity` should be false when `SeatsUsed <= SeatsTotal`.

ManyChat/admin copy:

```text
Listo. Agregamos mas seats a tu cuenta. Tus drivers pueden usar el mismo codigo de invitacion.
```

If payment fails:

```text
No pudimos cobrar el aumento de seats. La cantidad de seats no cambio.
```

What to verify in App Insights:

- `company.seat_quantity_change` with `outcome=completed`
- `reason=seat_quantity_increase_applied`
- `direction=increase`
- `effectiveWhen=immediate`

## 6. Reduce Company/Fleet Seats

This is the main company downgrade flow.

The company wants fewer seats. The danger is accidentally cutting off active drivers or letting billing and access disagree.

Endpoint:

```text
POST /api/stripe/subscription/update-seat-quantity
```

Request:

```json
{
  "actorUserPk": "{{actorUserPk}}",
  "actorUserId": "{{actorUserId}}",
  "companyId": "{{companyId}}",
  "subscriptionId": "{{subscriptionId}}",
  "targetSeats": 8,
  "effectiveWhen": "next_invoice"
}
```

Stripe behavior:

- The backend updates the subscription item quantity.
- It sends `proration_behavior=none`.
- It does not create a credit or immediate refund.
- Billing impact is expected on the next invoice.

Safety rule:

The backend rejects the decrease if:

```text
targetSeats < SeatsUsed
```

This prevents a company from reducing to fewer seats than active drivers currently use.

Backend behavior:

- Validates actor/company authorization.
- Loads current entitlement.
- Computes direction as `decrease`.
- Requires `effectiveWhen=next_invoice`.
- Rejects the request before Stripe if target seats are below active used seats.
- If safe, updates Stripe with no proration.
- Updates local `SeatsTotal` immediately so no new drivers can consume removed capacity.
- Emits App Insights logs and metric `company.seat_quantity_change`.

Access result:

- Existing active drivers stay active if the decrease is safe.
- New seat claims are limited by the lower `SeatsTotal`.
- No active driver should be randomly removed.

Unsafe decrease example:

Current seats:

```text
SeatsTotal = 10
SeatsUsed = 6
```

Request:

```text
targetSeats = 5
```

Result:

```json
{
  "ok": false,
  "error": "target_below_seats_used"
}
```

ManyChat/admin copy:

```text
Para bajar la cantidad de seats, primero revisemos cuantos drivers activos tienes para no cortar acceso por error.
```

What to verify in App Insights:

- Successful safe decrease:
  - `company.seat_quantity_change`
  - `outcome=completed`
  - `reason=seat_quantity_decrease_applied_safe`
  - `direction=decrease`
  - `effectiveWhen=next_invoice`

- Unsafe decrease:
  - `company.seat_quantity_change`
  - `outcome=failed`
  - `reason=target_below_seats_used`

## 7. Individual Access Plus Company Seat

This is not a pure upgrade or downgrade, but it is an important access transition.

A driver may have individual paid access and then receive company seat access. HabloTruck should not automatically cancel the individual subscription.

Business rule:

- Allow both sources.
- Access source becomes `Both`.
- The user stays `Full`.
- Offer a separate cancellation path if they want to stop individual renewal.

Why:

Company access may be temporary. Automatically canceling individual access could create accidental churn or leave the driver blocked later.

ManyChat copy:

```text
Listo. Tambien tienes acceso por tu empresa. Tu acceso sigue activo.
```

Optional follow-up:

```text
Si quieres, puedes revisar tu plan individual para decidir si lo mantienes o lo cancelas al final del periodo.
```

## 8. Company Seat To Individual Paid Access

This is an upgrade path from company dependency to individual ownership.

A driver may lose a company seat, leave a company, or want to keep access personally. HabloTruck should allow the driver to buy monthly or yearly individual access.

Business rule:

- Let the user buy an individual plan even if company access still exists.
- If both exist, access source becomes `Both`.
- If the company seat ends later, individual access should keep the user `Full`.

ManyChat copy:

```text
Puedes mantener tu acceso con un plan individual aunque ya no estes usando un seat de empresa.
```

## 9. Cancel Renewal At Period End

This is not exactly a plan downgrade, but from the user's point of view it is a downgrade/churn-control path.

Endpoint:

```text
POST /api/stripe/subscription/cancel-at-period-end
```

Behavior:

- Stripe marks the subscription to cancel at period end.
- HabloTruck should keep access `Full` until the paid-through date.
- Access should not become blocked immediately.
- When Stripe later sends cancellation/deletion events, backend recomputes access.

ManyChat copy:

```text
Listo. Tu renovacion quedo cancelada. Puedes seguir usando HabloTruck hasta el final de tu periodo pagado.
```

## Invalid Or Rejected Combinations

The backend intentionally rejects these combinations:

| Request | Why It Is Rejected |
| --- | --- |
| Yearly to monthly with `effectiveWhen=immediate` | Prevents confusing immediate downgrade behavior and possible paid-value loss. |
| Monthly to yearly with `effectiveWhen=period_end` | Current upgrade policy is immediate only. |
| Seat increase with `effectiveWhen=next_invoice` | Company expects capacity now; billing must succeed now. |
| Seat decrease with `effectiveWhen=immediate` | Current downgrade policy is no-proration next-invoice behavior. |
| Seat decrease below `SeatsUsed` | Prevents accidentally cutting off active drivers. |
| Plan change when Stripe customer does not match local user | Prevents one user from changing another user's subscription. |
| Plan change when the current Stripe price is not individual monthly/yearly | Prevents accidentally changing a company/fleet subscription into an individual plan. |
| Seat change when Stripe customer does not match company | Prevents company/customer ownership mismatch. |

## App Insights Signals

Individual plan changes:

```text
Metric: subscription.plan_change
Dimensions: outcome, reason, targetPlanType, effectiveWhen
```

Important reasons:

- `individual_upgrade_applied`
- `individual_downgrade_scheduled_period_end`
- `target_plan_already_active`
- `invalid_effective_when`
- `stripe_update_failed`
- `forbidden`

Company seat changes:

```text
Metric: company.seat_quantity_change
Dimensions: outcome, reason, direction, effectiveWhen
```

Important reasons:

- `seat_quantity_increase_applied`
- `seat_quantity_decrease_applied_safe`
- `target_quantity_already_active`
- `target_below_seats_used`
- `invalid_effective_when_for_increase`
- `invalid_effective_when_for_decrease`
- `stripe_update_failed`
- `forbidden`

Useful query file:

```text
docs/APP_INSIGHTS_METRICS_QUERIES.md
```

Use:

- `20. Subscription Plan Change Outcomes`
- `21. Company Seat Quantity Change Outcomes`
- `22. Subscription Change Trace`

## QA Checklist

For monthly to yearly:

- Endpoint returns `ok=true`.
- Response has `targetPlanType=individual_yearly`.
- Response has `effectiveWhen=immediate`.
- Stripe invoice succeeds.
- User remains `Full`.
- Metric reason is `individual_upgrade_applied`.

For yearly to monthly:

- Endpoint returns `ok=true`.
- Response has `targetPlanType=individual_monthly`.
- Response has `effectiveWhen=period_end`.
- A Stripe subscription schedule is created or updated.
- No immediate monthly charge is created.
- User remains `Full` while paid through.
- Metric reason is `individual_downgrade_scheduled_period_end`.

For seat increase:

- Endpoint returns `ok=true`.
- Response has `direction=increase`.
- Response has `effectiveWhen=immediate`.
- Stripe payment succeeds.
- Entitlement `SeatsTotal` increases.
- Metric reason is `seat_quantity_increase_applied`.

For safe seat decrease:

- Endpoint returns `ok=true`.
- Response has `direction=decrease`.
- Response has `effectiveWhen=next_invoice`.
- `targetSeats >= SeatsUsed`.
- Entitlement `SeatsTotal` decreases.
- Metric reason is `seat_quantity_decrease_applied_safe`.

For unsafe seat decrease:

- Endpoint returns `ok=false`.
- Response has `error=target_below_seats_used`.
- Stripe is not called.
- Entitlement `SeatsTotal` does not change.

## Product Note

Individual yearly-to-monthly now uses Stripe subscription schedule support.

The backend still does not store a separate local pending-plan-change record. Stripe is the source of truth for the scheduled phase, and webhook projection should update local user facts when the monthly phase actually starts.

Current schedule creation preserves the core HabloTruck billing shape: current price, target price, quantity, period dates, and no immediate proration. If HabloTruck later adds coupons, custom tax rates, trials, or subscription-level discounts to plan-change customers, the schedule phase mapping should be expanded and covered with Stripe integration tests before release.

Current yearly-to-monthly behavior should be described as:

```text
period-end scheduled downgrade
```
