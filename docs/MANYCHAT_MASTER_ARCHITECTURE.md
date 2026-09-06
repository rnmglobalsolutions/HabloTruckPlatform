# ManyChat Master Architecture

This document connects all HabloTruck ManyChat journeys into one operational architecture.

It explains:

- the top-level journeys
- how they connect
- what triggers each one
- which backend contracts support them
- where `Stay with HabloTruck` fits

## Main Journeys

HabloTruck should be modeled in ManyChat around five major journeys:

1. Individual subscription.
2. Company or school purchase.
3. Join with invite code.
4. Billing recovery and renewal.
5. Stay with HabloTruck / save before churn.

## Journey 1 - Individual Subscription

This journey is for:

- monthly buyers
- yearly buyers

Primary document:

- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md`

Primary backend contract:

- `POST /api/stripe/payment-link`

Main result:

- Stripe checkout is created
- user pays
- Stripe webhook projects access
- ManyChat receives access-state sync

## Journey 2 - Company or School Purchase

This journey is for:

- company owners
- school admins
- operators buying seats for teams

Primary document:

- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`

Primary backend contract:

- `POST /api/company/fleet/start-checkout`

Main result:

- company checkout is created
- admin pays
- webhook creates company and entitlement
- active invite is auto-created

## Journey 3 - Join with Invite Code

This journey is for:

- drivers
- students
- team members entering through a code

Primary document:

- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`

Primary backend contracts:

- `POST /api/company/invite/info`
- `POST /api/company/join`

Main result:

- code is validated
- user joins company
- seat is assigned
- access is synchronized back to ManyChat

## Journey 4 - Billing Recovery and Renewal

This journey is for:

- failed payments
- renewal reminders
- billing status checks
- update-card flows
- retry-payment flows

Primary document:

- `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md`

Primary backend contracts:

- `POST /api/stripe/subscription/payment-method-update-link`
- `POST /api/stripe/subscription/retry-payment`

Main result:

- user updates payment method
- backend retries or confirms recovery
- ManyChat reflects updated billing status

## Journey 5 - Stay with HabloTruck

This journey is specifically for retention.

It should be used when:

- renewal is approaching
- user looks at cancellation-related content
- user seems at risk of churn
- backend triggers a reminder journey configured as `save_before_churn`

Operationally, this journey lives on top of the billing and renewal system, but should be treated as its own conversational experience.

### Goal

Reinforce value before the user leaves.

### Message direction

The tone should be:

- helpful
- practical
- benefit-oriented

Not:

- desperate
- overly salesy
- generic

### Suggested outcomes

- keep current plan
- update payment method if needed
- re-engage with content
- talk to support

## Suggested User Lifecycle Map

### Stage 1: New visitor

Router options:

- individual access
- company or school
- already have code

### Stage 2: Pre-purchase

Options:

- monthly
- yearly
- company seats

### Stage 3: Active subscriber

Options:

- start learning
- view topics
- manage account

### Stage 4: At-risk subscriber

Options:

- renewal reminder
- stay with HabloTruck
- payment recovery

### Stage 5: Recovered subscriber

Options:

- resume learning
- confirm access
- continue full experience

## Recommended Master Flow Set

At the architecture level, the bot should have these top-level flows:

1. `Flow 0 - Entry Router`
2. `Flow - Individual Subscription`
3. `Flow - Company or School Purchase`
4. `Flow - Join with Invite Code`
5. `Flow - Billing Recovery`
6. `Flow - Renewal Reminder`
7. `Flow - Stay with HabloTruck`
8. `Flow - Mi Cuenta`

## Backend Connection Map

### Purchase creation

- `POST /api/stripe/payment-link`
- `POST /api/company/fleet/start-checkout`

### Company operations

- `POST /api/company/invite/active`
- `POST /api/company/invite/resend`
- `POST /api/company/invite/info`
- `POST /api/company/join`

### Billing recovery

- `POST /api/stripe/subscription/payment-method-update-link`
- `POST /api/stripe/subscription/retry-payment`

### Webhook-driven journeys

- Stripe webhook projection
- ManyChat access sync
- ManyChat billing recovery sync
- renewal and save-before-churn reminder dispatch

## Suggested Trigger Strategy

### Direct user triggers

- `info`
- `inicio`
- `menu`
- `ya pagué`
- `mi acceso`
- `mi código`
- `actualizar pago`

### Backend-triggered ManyChat flows

- payment failed
- renewal reminder
- payment recovery reminder
- save before churn

## Decision Rules

- If user has full access, send to content, not to sales.
- If user is in billing recovery, send to recovery before reselling.
- If user is company-backed, respect that path and avoid unnecessary individual upsell.
- If user already joined a company, treat repeated join attempts as verification, not as failure.
- If user says they already paid, verify access first.

## Recommended Operating Order

Build ManyChat in this order:

1. entry router and navigation
2. individual subscriptions
3. company and invite journeys
4. billing recovery and renewal
5. stay with HabloTruck retention flow

## Document Connections

- `docs/MANYCHAT_ENTRY_ROUTER_AND_NAVIGATION.md`
- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md`
- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`
- `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md`

## Key Operational Conclusion

HabloTruck should not be built as one giant flow.

It should be built as a modular conversation architecture:

- one router
- multiple specialized journeys
- backend-driven sync and reminders
- clear recovery and retention paths
