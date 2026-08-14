# ManyChat Tags and Custom Fields Guide

This document lists the main ManyChat tags and custom fields used by HabloTruck.

Its purpose is to answer five practical questions:

- what should exist in ManyChat before go-live
- what is updated by the backend
- what is only used by the ManyChat flows
- what each item is for
- which items should be created manually in ManyChat

## Recommended Rule

For HabloTruck, the safest approach is:

1. Create the required tags manually in ManyChat.
2. Create the required custom fields manually in ManyChat.
3. Let the backend update those tags and fields during runtime.

Even though the backend can add or remove tags by name and set custom fields by name, it is still better to have the workspace prepared in advance.

## Backend-Synchronized Tags

These tags are part of the backend integration and should be created in ManyChat before production use.

| Name | Type | Set By | Purpose | Create In ManyChat |
|---|---|---|---|---|
| `HT_ACCESS_FULL` | Tag | Backend | Marks a user with full active access | Yes |
| `HT_ACCESS_GRACE` | Tag | Backend | Marks a user in grace period | Yes |
| `HT_ACCESS_BLOCKED` | Tag | Backend | Marks a user without active access | Yes |
| `HT_SRC_INDIVIDUAL` | Tag | Backend | Marks access coming from an individual subscription | Yes |
| `HT_SRC_COMPANY` | Tag | Backend | Marks access coming from a company or school entitlement | Yes |
| `HT_BILLING_ACTION_REQUIRED` | Tag | Backend | Marks a user who needs billing recovery action | Yes |
| `HT_BILLING_RECOVERED` | Tag | Backend | Marks a user whose billing issue has been recovered | Yes |
| `HT_CANCEL_SCHEDULED` | Tag | Backend | Marks a subscription scheduled to cancel at period end; backend adds it when Stripe confirms `cancel_at_period_end=true` and removes it when cancellation is reverted or deleted | Yes |
| `HT_CHURNED` | Tag | Backend | Marks a user whose subscription deletion left effective access blocked | Yes |

`HT_CANCEL_SCHEDULED` is added after `customer.subscription.updated` when Stripe confirms `cancel_at_period_end=true`. It is removed after `customer.subscription.updated` with `cancel_at_period_end=false` or after `customer.subscription.deleted`.

`HT_CHURNED` is only added after `customer.subscription.deleted` when the backend's final `AccessDecision` is `Blocked`. If the deleted subscription does not remove effective access, for example because company access remains active, the backend removes `HT_CANCEL_SCHEDULED` but does not remove `HT_ACCESS_FULL` and does not add `HT_CHURNED`.

## Backend-Synchronized Custom Fields

These custom fields are updated by the backend and should also be created in ManyChat before production use.

| Name | Type | Set By | Purpose | Create In ManyChat |
|---|---|---|---|---|
| `ht_access_mode` | Text | Backend | Stores current access mode: `Full`, `Grace`, or `Blocked` | Yes |
| `ht_grace_ends_utc` | Text | Backend | Stores the grace period expiration in UTC | Yes |
| `ht_company_id` | Text | Backend | Stores the related company ID if company access applies | Yes |
| `ht_billing_recovery_status` | Text | Backend | Stores current billing recovery state | Yes |
| `ht_billing_recovery_subscription_id` | Text | Backend | Stores the Stripe subscription ID involved in recovery | Yes |
| `ht_billing_recovery_invoice_id` | Text | Backend | Stores the Stripe invoice ID involved in recovery | Yes |
| `ht_billing_recovery_invoice_status` | Text | Backend | Stores the latest invoice status during recovery | Yes |
| `ht_billing_recovery_updated_utc` | Text | Backend | Stores when the billing recovery state was last updated | Yes |
| `ht_billing_recovery_started_utc` | Text | Backend | Stores when the billing recovery journey started | Yes |

## ManyChat Helper Custom Fields

These are not primarily synchronized by the backend as domain state. They are helper fields used by the conversational flows.

They should also be created manually in ManyChat.

| Name | Type | Set By | Purpose | Create In ManyChat |
|---|---|---|---|---|
| `cf_email` | Text | ManyChat | Stores user email when needed in flows | Yes |
| `cf_phone_e164` | Text | ManyChat | Stores phone in normalized format when available | Yes |
| `cf_plan_type` | Text | ManyChat | Stores selected plan, such as `individual_monthly` or `individual_yearly` | Yes |
| `cf_checkout_url` | Text | ManyChat | Optional helper field for checkout link handling | Yes |
| `cf_checkout_session_id` | Text | ManyChat | Optional helper field to retain checkout session context | Yes |
| `cf_last_checkout_error` | Text | ManyChat | Stores last visible checkout error for retry logic | Yes |
| `cf_company_id` | Text | ManyChat / Backend response storage | Stores company ID returned by fleet checkout | Yes |
| `cf_company_name` | Text | ManyChat | Stores company or school name during B2B flows | Yes |
| `cf_requested_seats` | Number or Text | ManyChat | Stores requested number of seats | Yes |
| `cf_entitlement_id` | Text | ManyChat / Backend response storage | Stores returned entitlement ID when needed | Yes |
| `cf_invite_code` | Text | ManyChat / Backend response storage | Stores active invite code for join flows | Yes |
| `cf_last_company_error` | Text | ManyChat | Stores latest company-flow error for fallback UX | Yes |
| `cf_actor_user_pk` | Text | ManyChat | Stores actor PK for billing recovery or self-service flows | Yes |
| `cf_actor_user_id` | Text | ManyChat | Stores actor user ID for billing recovery or self-service flows | Yes |
| `cf_subscription_id` | Text | ManyChat | Stores subscription ID for account and billing flows | Yes |
| `cf_last_billing_error` | Text | ManyChat | Stores billing-related error shown to the user | Yes |
| `cf_payment_method_update_url` | Text | ManyChat | Stores billing portal or payment method update URL | Yes |

## Built-In ManyChat Contact Values

These do not need to be created manually because they already exist in ManyChat.

| Name | Type | Set By | Purpose | Create In ManyChat |
|---|---|---|---|---|
| `{{contact.id}}` | Built-in | ManyChat | Used as `manyChatSubscriberId` sent to the backend | No |
| `{{contact.email}}` | Built-in | ManyChat | Used when email is already known in the contact profile | No |

## What The Backend Actually Does

Operationally, the backend does the following:

- adds tags by name
- removes tags by name
- sets custom fields by name
- triggers configured ManyChat flows by `flow_ns` when applicable

This behavior is implemented in:

- `HabloTruckPlatform.Infrastructure/Integrations/ManyChat/ManyChatOptions.cs`
- `HabloTruckPlatform.Infrastructure/Integrations/ManyChat/ManyChatSyncClient.cs`

That means the backend assumes those names are valid and available in your ManyChat workspace.

## What Should Be Created Before Go-Live

At minimum, create these before launch:

### Required tags

- `HT_ACCESS_FULL`
- `HT_ACCESS_GRACE`
- `HT_ACCESS_BLOCKED`
- `HT_SRC_INDIVIDUAL`
- `HT_SRC_COMPANY`
- `HT_BILLING_ACTION_REQUIRED`
- `HT_BILLING_RECOVERED`
- `HT_CANCEL_SCHEDULED`
- `HT_CHURNED`

### Required backend fields

- `ht_access_mode`
- `ht_grace_ends_utc`
- `ht_company_id`
- `ht_billing_recovery_status`
- `ht_billing_recovery_subscription_id`
- `ht_billing_recovery_invoice_id`
- `ht_billing_recovery_invoice_status`
- `ht_billing_recovery_updated_utc`
- `ht_billing_recovery_started_utc`

### Required flow helper fields

- `cf_email`
- `cf_phone_e164`
- `cf_plan_type`
- `cf_checkout_url`
- `cf_checkout_session_id`
- `cf_last_checkout_error`
- `cf_company_id`
- `cf_company_name`
- `cf_requested_seats`
- `cf_entitlement_id`
- `cf_invite_code`
- `cf_last_company_error`
- `cf_actor_user_pk`
- `cf_actor_user_id`
- `cf_subscription_id`
- `cf_last_billing_error`
- `cf_payment_method_update_url`

## Recommended Data Types

If ManyChat asks you to choose a field type, use these simple defaults:

- IDs, URLs, plan names, statuses, timestamps: `Text`
- seat count: `Number` if convenient, otherwise `Text`

If you want to keep the setup simple, using `Text` for almost all custom fields is acceptable.

## Practical Recommendation

If you are setting up ManyChat from scratch, do this in order:

1. Create all backend-synchronized tags.
2. Create all backend-synchronized custom fields.
3. Create all flow helper custom fields.
4. Build the flows.
5. Test one end-to-end scenario:
   - individual monthly
   - company checkout
   - company join
   - payment recovery

## Bottom Line

The backend is responsible for updating tags and fields during runtime.

But you should still pre-create the main tags and custom fields in ManyChat so that:

- flow conditions are reliable
- support and debugging are easier
- backend sync behavior is predictable
- launch-day surprises are reduced
