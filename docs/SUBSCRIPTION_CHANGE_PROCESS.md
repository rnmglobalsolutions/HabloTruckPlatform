# Subscription Change Process

This file explains the implemented HabloTruck process for subscription upgrades, downgrades, and company seat quantity changes.

## Implemented Endpoints

### Individual Plan Change

```text
POST /api/stripe/subscription/change-plan
```

Used for:

- `individual_monthly` to `individual_yearly`
- `individual_yearly` to `individual_monthly`

Required body:

```json
{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "subscriptionId": "sub_123",
  "targetPlanType": "individual_yearly",
  "effectiveWhen": "immediate"
}
```

Supported `targetPlanType` values:

- `individual_monthly`
- `individual_yearly`

Supported `effectiveWhen` values:

- `immediate`
- `next_invoice`

Current rules:

- Monthly to yearly uses `immediate`.
- Yearly to monthly uses `next_invoice`.
- Monthly to yearly sends Stripe `proration_behavior=always_invoice` and `billing_cycle_anchor=now`.
- Immediate invoice changes also send Stripe `payment_behavior=error_if_incomplete` so local state is not updated when Stripe cannot collect the required payment.
- Yearly to monthly sends Stripe `proration_behavior=none`. This is not a Stripe subscription schedule; the subscription item is updated now and billing changes on the next invoice without proration.
- The endpoint validates that the actor owns the subscription/customer context before changing Stripe.

Legacy compatibility:

- Incoming `period_end`, `period-end`, and `renewal` are accepted as aliases for `next_invoice`.
- New ManyChat, Postman, and application clients should send `next_invoice`.

### Company Seat Quantity Change

```text
POST /api/stripe/subscription/update-seat-quantity
```

Used for:

- adding seats to a company/fleet subscription
- safely reducing seats when the new quantity is still greater than or equal to currently used seats

Required body:

```json
{
  "actorUserPk": "U_20260331",
  "actorUserId": "USER_123",
  "companyId": "C1",
  "subscriptionId": "sub_123",
  "targetSeats": 25,
  "effectiveWhen": "immediate"
}
```

Current rules:

- Seat increase uses `proration_behavior=always_invoice`.
- Safe seat decrease uses `proration_behavior=none`.
- Seat increase must use `effectiveWhen=immediate`.
- Seat decrease must use `effectiveWhen=next_invoice`.
- Seat increases use Stripe `payment_behavior=error_if_incomplete`; if Stripe cannot collect the immediate invoice, the endpoint fails and local seat capacity is not increased.
- Unsafe decrease is rejected if `targetSeats < SeatsUsed`.
- The actor must belong to the company or match the company admin email.
- The Stripe customer on the subscription must match the company Stripe customer when the local company has one.

## Individual Upgrade Flow

Example: monthly to yearly.

1. ManyChat or ManageApp calls `POST /api/stripe/subscription/change-plan`.
2. Backend loads the actor user.
3. Backend resolves the actor subscription.
4. Backend reads the current Stripe subscription.
5. Backend verifies Stripe customer ownership.
6. Backend maps `individual_yearly` to the configured yearly price ID.
7. Backend calls Stripe subscription update with `payment_behavior=error_if_incomplete`.
8. Backend stores local user subscription hints.
9. Stripe sends `customer.subscription.updated` and possibly invoice events.
10. Webhook projection remains the source of truth.

Expected response:

```json
{
  "ok": true,
  "subscriptionId": "sub_123",
  "previousPlanType": "individual_monthly",
  "targetPlanType": "individual_yearly",
  "effectiveWhen": "immediate",
  "stripePriceId": "price_yearly",
  "interval": "year",
  "subscriptionStatus": "active",
  "currentPeriodEndUtc": "2027-04-14T00:00:00.0000000Z",
  "cancelAtPeriodEnd": false,
  "requestedAtUtc": "2026-04-14T00:00:00.0000000Z",
  "error": null
}
```

## Individual Downgrade Flow

Example: yearly to monthly.

1. ManyChat or ManageApp calls `POST /api/stripe/subscription/change-plan`.
2. Backend validates the actor and subscription ownership.
3. Backend maps `individual_monthly` to the configured monthly price ID.
4. Backend updates Stripe with no proration.
5. Backend preserves the current paid-through period end from Stripe.
6. Backend stores local user subscription hints.
7. Stripe webhooks continue to reconcile final state.

Important:

- Access should remain `Full` while the user is paid through.
- The current period end is the guardrail that prevents accidental grace/blocking.
- ManyChat should describe this as a no-proration downgrade for the next invoice, not as a true scheduled Stripe phase.
- The backend does not create Stripe subscription schedules for this flow.

Expected response:

```json
{
  "ok": true,
  "subscriptionId": "sub_123",
  "previousPlanType": "individual_yearly",
  "targetPlanType": "individual_monthly",
  "effectiveWhen": "next_invoice",
  "stripePriceId": "price_monthly",
  "interval": "month",
  "subscriptionStatus": "active",
  "currentPeriodEndUtc": "2026-12-31T00:00:00.0000000Z",
  "cancelAtPeriodEnd": false,
  "requestedAtUtc": "2026-04-14T00:00:00.0000000Z",
  "error": null
}
```

## Company Seat Increase Flow

1. Admin calls `POST /api/stripe/subscription/update-seat-quantity`.
2. Backend validates actor/company authorization.
3. Backend reads the entitlement `ent_<subscriptionId>`.
4. Backend reads Stripe subscription and validates customer ownership.
5. Backend updates Stripe quantity with `always_invoice`.
6. Stripe must collect the immediate invoice successfully.
7. Backend updates local entitlement `SeatsTotal`.
8. Existing invite code can continue to be used.

Expected response:

```json
{
  "ok": true,
  "companyId": "C1",
  "entitlementId": "ent_sub_123",
  "subscriptionId": "sub_123",
  "previousSeatsTotal": 20,
  "targetSeats": 25,
  "seatsUsed": 18,
  "isOverCapacity": false,
  "direction": "increase",
  "effectiveWhen": "immediate",
  "currentPeriodEndUtc": "2026-05-14T00:00:00.0000000Z",
  "requestedAtUtc": "2026-04-14T00:00:00.0000000Z",
  "error": null
}
```

## Company Seat Decrease Flow

1. Admin calls `POST /api/stripe/subscription/update-seat-quantity`.
2. Backend validates actor/company authorization.
3. Backend checks `targetSeats >= SeatsUsed`.
4. If safe, backend updates Stripe quantity with no proration.
5. Backend updates local entitlement `SeatsTotal` immediately so no new drivers can consume seats the company chose to remove.
6. If unsafe, backend rejects the request and does not call Stripe.

Unsafe response:

```json
{
  "ok": false,
  "companyId": "C1",
  "entitlementId": "ent_sub_123",
  "subscriptionId": "sub_123",
  "previousSeatsTotal": 20,
  "targetSeats": 10,
  "seatsUsed": 18,
  "isOverCapacity": false,
  "direction": "decrease",
  "effectiveWhen": "next_invoice",
  "currentPeriodEndUtc": "",
  "requestedAtUtc": "2026-04-14T00:00:00.0000000Z",
  "error": "target_below_seats_used"
}
```

## App Insights Signals

Plan changes log:

- Operation name: `subscription_plan_change`
- HTTP operation name: `subscription_plan_change_http`
- Metric: `subscription.plan_change`
- Metric dimensions: `outcome`, `reason`, `targetPlanType`, `effectiveWhen`

Seat quantity changes log:

- Operation name: `company_seat_quantity_change`
- HTTP operation name: `company_seat_quantity_change_http`
- Metric: `company.seat_quantity_change`
- Metric dimensions: `outcome`, `reason`, `direction`, `effectiveWhen`

Useful KQL:

- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 20. Subscription Plan Change Outcomes`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 21. Company Seat Quantity Change Outcomes`
- `docs/APP_INSIGHTS_METRICS_QUERIES.md / 22. Subscription Change Trace`

## Operational Notes

- Stripe remains the billing authority.
- The backend stores local hints immediately after a successful Stripe update, but webhooks remain the source of truth.
- Immediate upgrade and seat-increase calls are considered successful only when Stripe accepts the update without an incomplete payment state.
- ManyChat access sync still depends on access state changes. A monthly/yearly plan change may not change access mode because the user usually remains `Full`.
- Company seat decreases below active usage require support/admin action before they can be applied.
