# ManyChat Cancel Subscription Flow

This guide describes how ManyChat should call HabloTruck to cancel an individual subscription renewal at the end of the current paid period.

## Endpoint

```text
POST https://<func>.azurewebsites.net/api/stripe/subscription/cancel-at-period-end?code=<FUNCTION_KEY>
```

Headers:

```text
x-api-key: <HTTP_API_KEY>
Content-Type: application/json
```

## Individual Request From ManyChat

Use the ManyChat contact ID as the primary identity.

```json
{
  "scope": "individual",
  "manyChatSubscriberId": "{{contact.id}}",
  "subscriptionId": "{{cf_subscription_id}}"
}
```

`subscriptionId` is optional. If it is empty or not available, the backend resolves the user by `manyChatSubscriberId` and uses the stored `StripeSubscriptionId`.

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

If `alreadyScheduled` is `true`, the subscription was already set to cancel at period end and no Stripe update was needed.

## What ManyChat Should Store

When `ok` is `true`, store:

- `cf_actor_user_pk` from `actorUserPk`
- `cf_actor_user_id` from `actorUserId`
- `cf_subscription_id` from `subscriptionId`
- optionally store `currentPeriodEndFormatted` or `effectivePeriodEndFormatted` for user-facing copy

Formatted date fields are returned as Spanish display strings, for example `30 de abril de 2026`. If the source date is missing or null, the formatted value is an empty string.

## User-Facing Copy

For `ok = true`:

```text
Listo. Tu renovacion quedo cancelada. Puedes seguir usando HabloTruck hasta el final de tu periodo pagado.
```

For `alreadyScheduled = true`:

```text
Tu renovacion ya estaba cancelada. Tu acceso sigue activo hasta el final del periodo pagado.
```

## Error Handling

Common errors:

- `actor_required`: request did not include `actorUserPk`/`actorUserId` or `manyChatSubscriberId`
- `actor_not_found`: backend could not resolve the ManyChat subscriber to a HabloTruck user
- `subscription_id_required`: resolved user does not have a stored Stripe subscription and none was provided
- `subscription_not_found`: Stripe did not find the subscription
- `forbidden`: subscription does not belong to the resolved user/customer
- `stripe_update_failed`: Stripe update failed

For user-facing flows, route these errors to support instead of retrying indefinitely.

## Backend Behavior

The backend:

1. Resolves the HabloTruck user by `manyChatSubscriberId`.
2. Uses the request `subscriptionId` if provided, otherwise uses the user's stored `StripeSubscriptionId`.
3. Fetches the subscription from Stripe.
4. Verifies the Stripe customer matches the resolved user when `StripeCustomerId` is known.
5. Updates Stripe with `cancel_at_period_end = true`.
6. Returns the resolved actor IDs and effective period end.

The cancellation is not immediate. Access should remain active until `currentPeriodEndUtc`.
