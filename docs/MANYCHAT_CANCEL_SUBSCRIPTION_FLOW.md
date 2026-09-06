# ManyChat Cancel Subscription Flow

This is the canonical detailed flow for canceling an individual HabloTruck subscription from ManyChat.

The cancellation is not immediate. HabloTruck schedules the Stripe subscription to cancel at the end of the current paid period. The user keeps access until Stripe later sends the final subscription deletion event.

## Scope

This flow is for individual subscriptions.

Use it when a ManyChat contact wants to stop renewal for a monthly or annual individual plan.

Do not use this flow to cancel company/fleet billing unless the caller is an authorized company actor and the request includes company context. Company/fleet cancellation has different authorization and entitlement behavior.

## Required ManyChat Tags

These tags should exist in ManyChat before production:

```text
HT_ACCESS_FULL
HT_ACCESS_GRACE
HT_ACCESS_BLOCKED
HT_SRC_INDIVIDUAL
HT_SRC_COMPANY
HT_CANCEL_SCHEDULED
HT_CHURNED
```

Backend behavior:

- `HT_CANCEL_SCHEDULED` is added when Stripe confirms `cancel_at_period_end=true`.
- `HT_CANCEL_SCHEDULED` is removed when Stripe confirms `cancel_at_period_end=false`.
- `HT_CANCEL_SCHEDULED` is removed when Stripe sends `customer.subscription.deleted`.
- `HT_CHURNED` is added only when the final access decision after `customer.subscription.deleted` is `Blocked`.
- `HT_ACCESS_FULL` is removed on deletion only if the final access decision is `Blocked`.

This avoids marking a user as churned if they still have access through company/fleet.

## Happy Path

1. User asks to cancel renewal in ManyChat.
2. ManyChat confirms intent with the user.
3. ManyChat calls HabloTruck cancel endpoint.
4. Backend resolves the user by `manyChatSubscriberId`.
5. Backend resolves the Stripe subscription:
   - uses `subscriptionId` from request if provided
   - otherwise uses the user's stored `StripeSubscriptionId`
6. Backend verifies subscription ownership.
7. Backend updates Stripe with `cancel_at_period_end=true`.
8. Backend returns the period end date.
9. ManyChat tells the user their renewal was cancelled and access remains active until the paid-through date.
10. Stripe sends `customer.subscription.updated` with `cancel_at_period_end=true`.
11. Backend syncs ManyChat and adds `HT_CANCEL_SCHEDULED`.
12. At the end of the period, Stripe sends `customer.subscription.deleted`.
13. Backend recomputes access.
14. If access is now blocked, backend removes active/scheduled tags and adds `HT_CHURNED`.

## Endpoint

```text
POST https://<func>.azurewebsites.net/api/stripe/subscription/cancel-at-period-end?code=<FUNCTION_KEY>
```

Headers:

```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

## Request From ManyChat

Recommended request:

```json
{
  "scope": "individual",
  "manyChatSubscriberId": "{{contact.id}}",
  "subscriptionId": "{{cf_subscription_id}}"
}
```

`subscriptionId` is optional. If it is empty or missing, the backend resolves the user by `manyChatSubscriberId` and uses the user's stored `StripeSubscriptionId`.

Minimal request:

```json
{
  "scope": "individual",
  "manyChatSubscriberId": "{{contact.id}}"
}
```

## Response Object

Successful response:

```json
{
  "ok": true,
  "scope": "individual",
  "actorUserPk": "HT_U_001",
  "actorUserId": "U1",
  "subscriptionId": "sub_123",
  "cancelAtPeriodEnd": true,
  "alreadyScheduled": false,
  "effectivePeriodEndUtc": "2026-04-30T00:00:00.0000000Z",
  "effectivePeriodEndFormatted": "30 de abril de 2026",
  "currentPeriodEndUtc": "2026-04-30T00:00:00.0000000Z",
  "currentPeriodEndFormatted": "30 de abril de 2026",
  "cancelRequestedAtUtc": "2026-03-31T22:00:00.0000000Z",
  "cancelRequestedAtFormatted": "31 de marzo de 2026",
  "canceledAtUtc": "",
  "canceledAtFormatted": "",
  "error": null
}
```

If `alreadyScheduled` is `true`, the subscription was already scheduled to cancel at period end and no new Stripe update was needed.

## What ManyChat Should Store

When `ok` is `true`, store:

- `cf_actor_user_pk` from `actorUserPk`
- `cf_actor_user_id` from `actorUserId`
- `cf_subscription_id` from `subscriptionId`
- `cf_subscription_period_end` from `currentPeriodEndFormatted`

The backend returns formatted date fields as Spanish display strings, for example:

```text
30 de abril de 2026
```

If the source date is missing or null, the formatted value is an empty string.

## ManyChat Conversation Flow

Recommended user flow:

1. User selects cancellation option.
2. ManyChat explains that cancellation stops the next renewal, not today's access.
3. ManyChat asks for final confirmation.
4. If user confirms, call the backend endpoint.
5. If `ok=true`, show success copy with `currentPeriodEndFormatted`.
6. If `alreadyScheduled=true`, show already-cancelled copy.
7. If the endpoint returns an error, route to support and do not retry indefinitely.

Suggested confirmation copy:

```text
Si cancelas ahora, tu renovacion se detiene, pero tu acceso sigue activo hasta el final de tu periodo pagado. Quieres continuar?
```

Success copy:

```text
Listo. Tu renovacion quedo cancelada. Puedes seguir usando HabloTruck hasta el {{currentPeriodEndFormatted}}.
```

Already scheduled copy:

```text
Tu renovacion ya estaba cancelada. Tu acceso sigue activo hasta el {{currentPeriodEndFormatted}}.
```

Support fallback copy:

```text
No pude completar la cancelacion automaticamente. Te voy a pasar con soporte para revisar tu cuenta.
```

## Tag Timeline

### Before cancellation

Typical active individual state:

```text
Added/current:
HT_ACCESS_FULL
HT_SRC_INDIVIDUAL

Removed/currently absent:
HT_ACCESS_GRACE
HT_ACCESS_BLOCKED
HT_SRC_COMPANY
HT_CANCEL_SCHEDULED
HT_CHURNED
```

### Immediately after endpoint success

The endpoint schedules cancellation in Stripe and returns success. Access remains full.

ManyChat should not need to manually add `HT_CANCEL_SCHEDULED`. The backend adds it after Stripe confirms the state through webhook.

Expected access state remains:

```text
HT_ACCESS_FULL
HT_SRC_INDIVIDUAL
```

### After `customer.subscription.updated` with `cancel_at_period_end=true`

Backend syncs:

```text
Add:
HT_CANCEL_SCHEDULED

Keep:
HT_ACCESS_FULL
HT_SRC_INDIVIDUAL

Remove/absent:
HT_ACCESS_GRACE
HT_ACCESS_BLOCKED
HT_CHURNED
```

### If cancellation is reversed

If Stripe sends `customer.subscription.updated` with `cancel_at_period_end=false`, backend syncs:

```text
Remove:
HT_CANCEL_SCHEDULED

Keep:
HT_ACCESS_FULL
HT_SRC_INDIVIDUAL
```

### At period end after `customer.subscription.deleted`

If the user has no other active access, backend syncs:

```text
Add:
HT_ACCESS_BLOCKED
HT_CHURNED

Remove:
HT_ACCESS_FULL
HT_ACCESS_GRACE
HT_SRC_INDIVIDUAL
HT_SRC_COMPANY
HT_CANCEL_SCHEDULED
```

If the user still has company/fleet access, backend syncs:

```text
Remove:
HT_CANCEL_SCHEDULED

Do not add:
HT_CHURNED

Do not remove if effective access is still Full:
HT_ACCESS_FULL
```

## Monthly Example

User cancels a monthly subscription on day 15.

1. Backend schedules cancellation at period end.
2. User keeps `HT_ACCESS_FULL`.
3. Stripe confirms `cancel_at_period_end=true`.
4. Backend adds `HT_CANCEL_SCHEDULED`.
5. At the end of the month, Stripe sends `customer.subscription.deleted`.
6. If no other access exists, backend adds `HT_CHURNED` and `HT_ACCESS_BLOCKED`, then removes `HT_ACCESS_FULL` and `HT_CANCEL_SCHEDULED`.

## Annual Example

User cancels an annual subscription with 1 month remaining.

1. Backend schedules cancellation at annual period end.
2. User keeps `HT_ACCESS_FULL` for the remaining month.
3. Stripe confirms `cancel_at_period_end=true`.
4. Backend adds `HT_CANCEL_SCHEDULED`.
5. At annual period end, Stripe sends `customer.subscription.deleted`.
6. If no other access exists, backend marks the user churned and blocked.

## Backend Webhook Behavior

### `customer.subscription.updated`

When Stripe sends `cancel_at_period_end=true`:

- backend stores `StripeCancelAtPeriodEnd=true`
- backend keeps access full while `current_period_end` is in the future
- backend syncs `HT_CANCEL_SCHEDULED`

When Stripe sends `cancel_at_period_end=false`:

- backend stores `StripeCancelAtPeriodEnd=false`
- backend removes `HT_CANCEL_SCHEDULED`

### `customer.subscription.deleted`

When Stripe sends the final deletion:

- backend marks local subscription status as `deleted`
- backend recomputes effective access
- backend removes `HT_CANCEL_SCHEDULED`
- if effective access is `Blocked`, backend removes `HT_ACCESS_FULL` and adds `HT_CHURNED`
- if effective access is still `Full`, backend does not add `HT_CHURNED`

## Error Handling

Common endpoint errors:

- `actor_required`: request did not include `actorUserPk`/`actorUserId` or `manyChatSubscriberId`
- `actor_not_found`: backend could not resolve the ManyChat subscriber to a HabloTruck user
- `subscription_id_required`: resolved user does not have a stored Stripe subscription and none was provided
- `subscription_not_found`: Stripe did not find the subscription
- `forbidden`: subscription does not belong to the resolved user/customer
- `stripe_update_failed`: Stripe update failed

ManyChat should route these errors to support instead of retrying indefinitely.

## Production Checklist

- Create `HT_CANCEL_SCHEDULED` in ManyChat.
- Create `HT_CHURNED` in ManyChat.
- Confirm Stripe webhook includes `customer.subscription.updated`.
- Confirm Stripe webhook includes `customer.subscription.deleted`.
- Confirm ManyChat action passes `manyChatSubscriberId={{contact.id}}`.
- Prefer passing `subscriptionId={{cf_subscription_id}}`, but allow it to be blank.
- Store `currentPeriodEndFormatted` for user-facing copy.
- Do not manually add `HT_CHURNED` from ManyChat.
- Do not manually remove `HT_ACCESS_FULL` from ManyChat during cancel scheduling.
