# ManyChat Flows

This document describes the three supported user journeys that can start from ManyChat and end in HabloTruck backend flows.

## Purpose

HabloTruck currently supports three enrollment/commercial journeys:

1. Individual monthly subscription.
2. Individual yearly subscription.
3. Company or fleet purchase followed by seat assignment through invite codes.

This document explains:

- Which backend endpoints are involved.
- What payloads ManyChat should send.
- What responses ManyChat should expect.
- Where the current implementation is complete.
- Which remaining gaps are still product, admin UX, or future work.

## Domain Model

The company-seat flow depends on four concepts:

- `Company`: the B2B customer account or tenant.
- `Entitlement`: the concrete package of seats owned by that company.
- `InviteCode`: the code that allows drivers to claim one seat from a specific entitlement.
- `SeatAssignment`: the record that links a user to one seat within an entitlement.

In practice:

- A company buys access.
- The backend creates or updates an entitlement with a seat count.
- An invite code is created for that entitlement.
- Drivers use the invite code to join the company and occupy one seat.

## Shared ManyChat Fields

Recommended ManyChat custom fields:

- `cf_email`
- `cf_phone_e164`
- `cf_plan_type`
- `cf_company_id`
- `cf_company_name`
- `cf_requested_seats`
- `cf_entitlement_id`
- `cf_invite_code`

Recommended built-in contact value:

- `{{contact.id}}` as `manyChatSubscriberId`

## Shared Backend Endpoints

The main endpoints used by ManyChat are:

- `POST /api/stripe/payment-link`
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

## Flow 1: Individual Monthly

### Goal

Allow a user to subscribe directly to an individual monthly plan from ManyChat.

### Suggested ManyChat Steps

1. Ask the user whether they want monthly or yearly.
2. Store `cf_plan_type = individual_monthly`.
3. Ask for email if not already known.
4. Ask for phone if needed.
5. Call the checkout-link endpoint.
6. Send the returned Stripe URL to the user.
7. After payment success, continue follow-up messaging.

### External Request

Endpoint:

```text
POST /api/stripe/payment-link
```

Payload:

```json
{
  "planType": "individual_monthly",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "successUrl": "https://tu-frontend.com/success",
  "cancelUrl": "https://tu-frontend.com/cancel",
  "quantity": 1
}
```

### Expected Response

Successful response shape:

```json
{
  "result": true,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_...",
  "error": null
}
```

If `result = true`, ManyChat should:

- Show a button or URL to complete payment.

If `result = false`, ManyChat should:

- Show a failure message.
- Offer retry.

### Backend Behavior

- Stripe checkout session is created.
- Stripe webhook receives the payment/subscription event.
- The backend stores the user Stripe facts.
- The backend computes effective access.

The webhook is the source of truth for the final subscription projection.

## Flow 2: Individual Yearly

### Goal

Allow a user to subscribe directly to an individual yearly plan from ManyChat.

### Suggested ManyChat Steps

This flow is identical to the monthly flow except for the selected plan type.

### External Request

Endpoint:

```text
POST /api/stripe/payment-link
```

Payload:

```json
{
  "planType": "individual_yearly",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "successUrl": "https://tu-frontend.com/success",
  "cancelUrl": "https://tu-frontend.com/cancel",
  "quantity": 1
}
```

### Expected Response

Same as monthly:

```json
{
  "result": true,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_...",
  "error": null
}
```

### Backend Behavior

- Stripe checkout session is created.
- Stripe webhook projects the subscription.
- Access is recalculated after checkout and subsequent webhook events.

## Flow 3: Company or Fleet Seats

This journey has two separate parts:

1. The company admin purchases seats.
2. Drivers claim those seats using invite codes.

### Part A: Company Admin Purchases Seats

#### Goal

Allow an admin or owner to buy a seat bundle for the company.

#### Suggested ManyChat Steps

1. Ask for company name.
2. Ask how many seats are needed.
3. Call the dedicated fleet start-checkout endpoint.
4. Save the returned `companyId` in ManyChat.
5. Send the Stripe checkout URL to the admin.

#### External Request

Endpoint:

```text
POST /api/company/fleet/start-checkout
```

Payload:

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

#### Expected Response

Same checkout response shape:

```json
{
  "ok": true,
  "companyId": "C_01JQ...",
  "companyName": "Acme Trucking",
  "seats": 20,
  "url": "https://checkout.stripe.com/...",
  "sessionId": "cs_..."
}
```

#### Backend Behavior

When Stripe confirms the fleet purchase:

- The backend creates or updates the `Company`.
- The backend creates or updates the `Entitlement`.
- The seat count is stored in `SeatsTotal`.
- The backend auto-creates an active invite for that entitlement.

### Part B: Admin Retrieves or Resends the Invite

#### Goal

Retrieve the active invite code that was auto-created for the purchased entitlement.

#### External Request

Endpoint:

```text
POST /api/company/invite/active
```

Payload:

```json
{
  "companyId": "{{cf_company_id}}"
}
```

#### Expected Response

```json
{
  "ok": true,
  "code": "HT-AB12CD",
  "companyId": "C1",
  "entitlementId": "E1",
  "status": "active",
  "maxUses": 20,
  "uses": 4,
  "remaining": 16,
  "expiresAtUtc": "2026-04-30T00:00:00.0000000Z"
}
```

If ManyChat needs to show the code again later, it can call:

```text
POST /api/company/invite/resend
```

Payload:

```json
{
  "companyId": "{{cf_company_id}}",
  "entitlementId": "{{cf_entitlement_id}}"
}
```

This endpoint returns the existing active invite when present, or recreates one from the entitlement if it is missing.

ManyChat can then:

- Show the invite code to the admin.
- Save it into `cf_invite_code`.
- Re-display it later from a "show my code again" branch.

### Part C: Driver Uses Invite Code to Claim a Seat

#### Goal

Allow an individual driver to join the company by consuming one invite use and occupying one entitlement seat.

#### Suggested ManyChat Steps

1. Ask whether the driver already has a company code.
2. Store that code in `cf_invite_code`.
3. Validate the code.
4. If valid, collect or confirm email.
5. Call the join endpoint.
6. Show success or failure.

#### Step 1: Validate Invite

Endpoint:

```text
POST /api/company/invite/info
```

Payload:

```json
{
  "inviteCode": "{{cf_invite_code}}"
}
```

Expected response:

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

ManyChat logic:

- If `valid = false`, stop and show an error message.
- If `valid = true`, continue to join.

#### Step 2: Join Company

Endpoint:

```text
POST /api/company/join
```

Payload:

```json
{
  "inviteCode": "{{cf_invite_code}}",
  "email": "{{contact.email}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "phoneE164": "{{cf_phone_e164}}"
}
```

Expected successful response:

```json
{
  "ok": true,
  "alreadyJoined": false,
  "companyId": "C1",
  "entitlementId": "E1"
}
```

Possible business outcomes:

- `ok = true` and `alreadyJoined = false`
- `ok = true` and `alreadyJoined = true`
- `ok = false` with an error such as:
  - invite not found
  - invite expired
  - invite exhausted
  - no seats available
  - user already in another company

#### Backend Behavior

On a successful join:

- The backend loads the invite.
- The backend resolves or creates the user.
- The backend consumes one invite use.
- The backend reserves one entitlement seat.
- The backend creates or activates the seat assignment.
- The backend updates the user's company facts.
- The backend recalculates effective access.

## Suggested ManyChat Trees

### Entry Tree A: Individual Purchase

User choice:

- Monthly
- Yearly

Actions:

- Set plan field.
- Call checkout endpoint.
- Send Stripe link.

### Entry Tree B: Company Admin Purchase

User choice:

- I want seats for my company.

Actions:

- Ask company name.
- Ask seats needed.
- Call fleet checkout endpoint.
- Send Stripe link.
- After payment, call the active-invite endpoint and show the code.

### Entry Tree C: Driver Join by Company Code

User choice:

- I already have a company invite code.

Actions:

- Ask invite code.
- Validate invite.
- Ask or confirm identity.
- Call join endpoint.
- Show outcome.

## Current Manual or Remaining Gaps

The backend already covers the core automation:

- Fleet checkout creates or updates `Company`.
- Fleet checkout creates or updates the active `Entitlement`.
- Fleet checkout auto-creates an active invite for that entitlement.
- Admin flows can fetch the active invite again through `/api/company/invite/active`.
- Admin flows can recover or recreate the active invite through `/api/company/invite/resend`.

The remaining gaps are mostly around product UX or convenience:

- ManyChat still needs to store the returned `companyId` so the admin can fetch the invite later without friction.
- If product wants invite recovery without `companyId`, a lookup by admin identity would still help.
- If product wants admins to manage multiple active entitlements, listing endpoints would still help.
- The generic function readme files do not document these product journeys.

## Recommended Next Backend Improvements

To make the ManyChat experience fully end-to-end:

1. Expose the created `entitlementId` more directly to the admin journey if ManyChat needs to target a specific entitlement.
2. Add an admin-friendly endpoint to list active entitlements by company.
3. Add admin identity lookup if product wants "show me my company code" without storing `companyId`.
4. Add a dedicated "register company" orchestration endpoint if product wants clearer B2B onboarding semantics.

## Recommended ManyChat Message Copy

### Individual Purchase

- "Choose your HabloTruck plan."
- "Complete your payment here."
- "Your access is being activated."

### Company Purchase

- "How many drivers do you want to cover?"
- "Complete your company checkout here."
- "Your seat package is ready."

### Driver Invite Flow

- "Enter your company access code."
- "Your code is valid. Let's finish your registration."
- "You are now linked to your company's HabloTruck access."

## Operational Summary

The intended business and technical flow is:

- Individual users buy their own monthly or yearly access.
- Companies buy seat bundles through the dedicated fleet checkout endpoint.
- Stripe webhook creates or updates the company subscription projection.
- The backend auto-creates an active invite for the purchased entitlement.
- Admins can retrieve that code again with `invite/active` or `invite/resend`.
- Drivers use invite codes to consume those company seats.

That is the current shape of the backend implementation.

## ManyChat Blueprint

This section translates the operational summary into the minimum ManyChat flows needed to run HabloTruck.

### Flow 0: Entry Router

Purpose:

- Route the user into the correct commercial journey as fast as possible.

Suggested opening message:

- "Welcome to HabloTruck. How do you want to get started?"

Buttons:

- `I am a driver`
- `I am a company or school`
- `I already have a company code`

Routing:

- `I am a driver` -> `Flow 1: Individual Plan Picker`
- `I am a company or school` -> `Flow 4: Fleet Admin Checkout`
- `I already have a company code` -> `Flow 7: Join With Invite Code`

### Flow 1: Individual Plan Picker

Purpose:

- Let a driver choose monthly or yearly access.

Suggested message:

- "Choose the HabloTruck plan that fits you best."

Buttons:

- `Monthly`
- `Yearly`

Actions:

- If `Monthly`: set `cf_plan_type = individual_monthly`
- If `Yearly`: set `cf_plan_type = individual_yearly`

Next:

- Both options go to `Flow 2: Collect Individual Data`

### Flow 2: Collect Individual Data

Purpose:

- Capture the minimum information required for checkout.

Blocks:

- Ask for email if `{{contact.email}}` is empty.
- Ask for phone if `cf_phone_e164` is empty.

Suggested messages:

- "What email should we use for your access?"
- "What phone number should we save for your account?"

Next:

- Go to `Flow 3: Create Individual Checkout`

### Flow 3: Create Individual Checkout

Purpose:

- Generate the Stripe checkout link for an individual buyer.

External Request:

```text
POST /api/stripe/payment-link
```

Payload:

```json
{
  "planType": "{{cf_plan_type}}",
  "email": "{{contact.email}}",
  "phoneE164": "{{cf_phone_e164}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "successUrl": "https://tu-frontend.com/success",
  "cancelUrl": "https://tu-frontend.com/cancel",
  "quantity": 1
}
```

Conditions:

- If `result = true`: show payment button with `url`
- If `result = false`: go to `Flow 10: Error and Retry`

Suggested success message:

- "Your checkout link is ready. Complete your payment here."

Next:

- Optional: send to `Flow 11: Post-Checkout Confirmation`

### Flow 4: Fleet Admin Checkout

Purpose:

- Let a company owner or school admin buy seats with the least possible friction.

Blocks:

- Ask for company or school name.
- Ask for number of seats.
- Ask for email if needed.
- Ask for phone if needed.

Suggested messages:

- "What is the name of your company or school?"
- "How many drivers or students do you want to cover?"
- "What email should we use for your admin account?"

Store:

- `cf_company_name`
- `cf_requested_seats`
- `cf_email`
- `cf_phone_e164`

Next:

- Go to `Flow 5: Start Fleet Checkout`

### Flow 5: Start Fleet Checkout

Purpose:

- Create the B2B checkout session and return a `companyId` that ManyChat can keep.

External Request:

```text
POST /api/company/fleet/start-checkout
```

Payload:

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

Actions on success:

- Save `companyId` into `cf_company_id`
- Show button using returned `url`

Suggested success message:

- "Your company checkout is ready. Complete payment here."

Conditions:

- If `ok = true`: continue to Stripe checkout
- If `ok = false`: go to `Flow 10: Error and Retry`

Next:

- After successful payment, go to `Flow 6: Show Admin Invite`

### Flow 6: Show Admin Invite

Purpose:

- Retrieve the active invite created automatically after the fleet purchase.

External Request:

```text
POST /api/company/invite/active
```

Payload:

```json
{
  "companyId": "{{cf_company_id}}"
}
```

Actions on success:

- Save `code` into `cf_invite_code`
- Save `entitlementId` into `cf_entitlement_id`

Suggested message:

- "Your team code is `{{cf_invite_code}}`. Share it with your drivers or students so they can join your HabloTruck access."

Conditions:

- If `ok = true`: show the code
- If `ok = false`: offer retry or go to support

Next:

- Optional button: `Show my code again` -> `Flow 9: Resend Admin Invite`

### Flow 7: Join With Invite Code

Purpose:

- Allow a driver or student to claim a company seat with an invite code.

Blocks:

- Ask for invite code.
- Store into `cf_invite_code`.

Suggested message:

- "Enter your company access code."

Next:

- Go to `Flow 8: Validate and Join`

### Flow 8: Validate and Join

Purpose:

- Validate the invite first, then complete the join request.

Step A: validate invite

External Request:

```text
POST /api/company/invite/info
```

Payload:

```json
{
  "inviteCode": "{{cf_invite_code}}"
}
```

Conditions after validation:

- If `valid = false`: go to `Flow 10: Error and Retry`
- If `valid = true`: continue

Step B: collect identity

- Ask for email if missing
- Ask for phone if missing

Step C: join company

External Request:

```text
POST /api/company/join
```

Payload:

```json
{
  "inviteCode": "{{cf_invite_code}}",
  "email": "{{contact.email}}",
  "manyChatSubscriberId": "{{contact.id}}",
  "phoneE164": "{{cf_phone_e164}}"
}
```

Conditions after join:

- If `ok = true` and `alreadyJoined = false`: show success
- If `ok = true` and `alreadyJoined = true`: show already linked message
- If `ok = false`: go to `Flow 10: Error and Retry`

Suggested success message:

- "You are now connected to your company's HabloTruck access."

### Flow 9: Resend Admin Invite

Purpose:

- Let the admin recover the active company code without buying again.

External Request:

```text
POST /api/company/invite/resend
```

Payload:

```json
{
  "companyId": "{{cf_company_id}}",
  "entitlementId": "{{cf_entitlement_id}}"
}
```

Conditions:

- If `ok = true`: update `cf_invite_code` and show it again
- If `ok = false`: go to `Flow 10: Error and Retry`

Suggested message:

- "Here is your current team code again: `{{cf_invite_code}}`."

### Flow 10: Error and Retry

Purpose:

- Handle the most common operational failures without dead-ending the user.

Recommended cases:

- Payment link could not be created
- Invite code invalid
- Invite code expired
- Invite code exhausted
- No seats available
- Company invite not found

Suggested message:

- "We could not complete that step right now. You can try again or contact support."

Buttons:

- `Try again`
- `Talk to support`
- `Main menu`

### Flow 11: Post-Checkout Confirmation

Purpose:

- Give the user a clean success state after payment.

Individual suggested message:

- "Your payment was received. We are activating your HabloTruck access now."

Company suggested message:

- "Your company purchase was completed. Your team code is ready."

Recommended buttons:

- Individual: `Main menu`
- Company: `Show my code`

## Minimum Required Flow Set

If you want the smallest production-ready ManyChat setup, implement these first:

1. `Flow 0: Entry Router`
2. `Flow 1: Individual Plan Picker`
3. `Flow 2: Collect Individual Data`
4. `Flow 3: Create Individual Checkout`
5. `Flow 4: Fleet Admin Checkout`
6. `Flow 5: Start Fleet Checkout`
7. `Flow 6: Show Admin Invite`
8. `Flow 7: Join With Invite Code`
9. `Flow 8: Validate and Join`
10. `Flow 10: Error and Retry`

`Flow 9: Resend Admin Invite` and `Flow 11: Post-Checkout Confirmation` are strongly recommended, but they can be added right after the core flows are live.
