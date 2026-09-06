# Application Insights Queries

This document contains ready-to-use KQL queries for `Application Insights > Logs`.

Use these queries to monitor:

- company joins
- seat refresh behavior
- ManyChat queue throughput
- fallback and retry behavior
- Stripe event intake
- access decision volume
- exceptions and dependency failures

## How To Use

1. Open the target `Application Insights` resource in Azure Portal.
2. Go to `Logs`.
3. Paste one query at a time.
4. Adjust the portal time range as needed.

Recommended first ranges:

- `Last 1 hour` for troubleshooting
- `Last 24 hours` for trend checks
- `Last 7 days` for operational patterns

## 1. Company Join Outcomes

Purpose:
See how many company joins completed versus how many were denied, and why.

```kusto
customMetrics
| where name == "company.join"
| extend outcome = tostring(customDimensions["outcome"])
| extend reason = tostring(customDimensions["reason"])
| summarize total = sum(value) by outcome, reason
| order by total desc
```

## 2. Company Join Timeline

Purpose:
See join activity over time.

```kusto
customMetrics
| where name == "company.join"
| extend outcome = tostring(customDimensions["outcome"])
| summarize total = sum(value) by bin(timestamp, 15m), outcome
| order by timestamp asc
```

## 3. Seat Refresh Usage

Purpose:
See how often the join flow had to refresh `SeatsUsed` before attempting a reservation.
This helps verify that recounts are being skipped when there is enough headroom.

```kusto
customMetrics
| where name == "company.join.seat_refresh"
| extend refreshed = tostring(customDimensions["refreshed"])
| extend reason = tostring(customDimensions["reason"])
| summarize total = sum(value) by refreshed, reason
| order by total desc
```

## 4. ManyChat Dispatch Queue Volume

Purpose:
See how many ManyChat operations are being enqueued by action type.

```kusto
customMetrics
| where name == "manychat.dispatch.queued"
| extend actionType = tostring(customDimensions["actionType"])
| summarize total = sum(value) by actionType
| order by total desc
```

## 5. ManyChat Dispatch Processing Outcomes

Purpose:
See whether queued ManyChat work completed successfully or had to fall back.

```kusto
customMetrics
| where name == "manychat.dispatch.processed"
| extend actionType = tostring(customDimensions["actionType"])
| extend outcome = tostring(customDimensions["outcome"])
| summarize total = sum(value) by actionType, outcome
| order by total desc
```

## 6. ManyChat Queue Timeline

Purpose:
See enqueue versus processing rates over time.

```kusto
customMetrics
| where name in ("manychat.dispatch.queued", "manychat.dispatch.processed")
| summarize total = sum(value) by bin(timestamp, 15m), name
| order by timestamp asc
```

## 6A. Payment To ManyChat Access Sync Trace

Purpose:
Trace whether a successful Stripe payment produced an access recompute and queued/sent a ManyChat access sync.

Expected healthy sequence for an individual payment:

1. `stripe_checkout_completed`
2. `access_recompute`
3. `access_rules_decide` with `Mode=Full`
4. `manychat.dispatch.queued` with `actionType=manychat_sync`
5. `timer_process_manychat_dispatch_queue`
6. `manychat.dispatch.processed` with `actionType=manychat_sync` and `outcome=completed`
7. `manychat_sync_user_access` with `manychat_access_synced`

```kusto
traces
| where timestamp > ago(24h)
| extend operationName = tostring(customDimensions["OperationName"])
| extend decision = tostring(customDimensions["Decision"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend mode = tostring(customDimensions["Mode"])
| extend source = tostring(customDimensions["Source"])
| extend userId = tostring(customDimensions["UserId"])
| extend stripeEventId = tostring(customDimensions["StripeEventId"])
| extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
| extend subscriptionId = tostring(customDimensions["SubscriptionId"])
| where operationName in (
    "stripe_checkout_completed",
    "stripe_subscription_reduce",
    "access_recompute",
    "manychat_sync_user_access",
    "timer_process_manychat_dispatch_queue")
    or decision in (
        "access_rules_decide",
        "manychat_sync_skipped",
        "user_resolution")
    or message has "manychat"
| project timestamp, operationName, decision, outcome, reason, mode, source, userId, stripeCustomerId, subscriptionId, stripeEventId, message
| order by timestamp asc
```

## 6B. Did Access Sync Get Queued And Processed

Purpose:
Confirm whether access-state sync work was queued and completed after payment.

```kusto
customMetrics
| where timestamp > ago(24h)
| where name in ("manychat.dispatch.queued", "manychat.dispatch.processed")
| extend actionType = tostring(customDimensions["actionType"])
| extend outcome = tostring(customDimensions["outcome"])
| where actionType == "manychat_sync"
| summarize total = sum(value) by bin(timestamp, 5m), name, actionType, outcome
| order by timestamp asc
```

## 6C. Why ManyChat Access Sync Was Skipped

Purpose:
Find cases where payment/access was processed but ManyChat was not updated because the backend had no subscriber target or thought the access state had already been synced.

Most important reasons:

- `missing_manychat_audience`: the user does not have a ManyChat subscriber ID / external identity.
- `access_state_unchanged`: the backend decided no new sync was needed.

```kusto
traces
| where timestamp > ago(24h)
| extend decision = tostring(customDimensions["Decision"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend userId = tostring(customDimensions["UserId"])
| extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
| extend subscriptionId = tostring(customDimensions["SubscriptionId"])
| where decision == "manychat_sync_skipped"
| project timestamp, userId, stripeCustomerId, subscriptionId, outcome, reason, message
| order by timestamp desc
```

## 6D. ManyChat Sync For One Subscriber Suffix

Purpose:
If you know the last 4 digits/characters of the ManyChat subscriber ID, verify whether access sync started and completed for that subscriber.

Replace `1234` with the subscriber ID suffix shown in ManyChat.

```kusto
traces
| where timestamp > ago(24h)
| extend operationName = tostring(customDimensions["OperationName"])
| extend subscriberIdSuffix = tostring(customDimensions["SubscriberIdSuffix"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend mode = tostring(customDimensions["Mode"])
| extend source = tostring(customDimensions["Source"])
| where operationName == "manychat_sync_user_access"
| where subscriberIdSuffix == "1234"
| project timestamp, subscriberIdSuffix, mode, source, outcome, reason, message, customDimensions
| order by timestamp desc
```

## 6E. ManyChat API Calls And Failures

Purpose:
See whether the backend attempted ManyChat HTTP calls and whether ManyChat rejected or timed out.

```kusto
traces
| where timestamp > ago(24h)
| extend dependencyType = tostring(customDimensions["DependencyType"])
| extend dependencyOperation = tostring(customDimensions["DependencyOperation"])
| extend target = tostring(customDimensions["Target"])
| extend success = tostring(customDimensions["Success"])
| extend statusCode = tostring(customDimensions["StatusCode"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| where dependencyType == "manychat"
    or target has "ManyChat"
    or target has "manychat"
    or message has "ManyChat"
| project timestamp, dependencyOperation, target, success, statusCode, outcome, reason, message, customDimensions
| order by timestamp desc
```

## 6F. ManyChat Dispatch Timer Health

Purpose:
Confirm the queue processor timer is running every minute.

If `manychat_sync` is queued but not processed, start here.

```kusto
traces
| where timestamp > ago(24h)
| extend operationName = tostring(customDimensions["OperationName"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| where operationName == "timer_process_manychat_dispatch_queue"
| project timestamp, outcome, reason, message, customDimensions
| order by timestamp desc
```

## 6G. ManyChat Sync Failed Actions

Purpose:
Find ManyChat access sync work that moved to the failed-action retry system.

```kusto
customMetrics
| where timestamp > ago(24h)
| where name in ("failed.actions.queued", "failed.actions.retried")
| extend actionType = tostring(customDimensions["actionType"])
| where actionType == "manychat_sync"
| summarize total = sum(value) by name, actionType
| order by total desc
```

## 6H. Stripe Webhook Requests

Purpose:
If `6B` and `6C` are empty, confirm whether Stripe webhooks are reaching the Function App at all.

```kusto
requests
| where timestamp > ago(24h)
| where name has "StripeWebhook" or url has "/api/stripe/webhook"
| project timestamp, name, url, resultCode, success, duration, operation_Id
| order by timestamp desc
```

## 6I. Stripe Event Types Received

Purpose:
Confirm which Stripe events the backend received. For individual checkout you usually want to see at least `checkout.session.completed`; for subscription state you may also see `invoice.paid` and/or `customer.subscription.updated`.

```kusto
customMetrics
| where timestamp > ago(24h)
| where name == "stripe.events.received"
| extend eventType = tostring(customDimensions["eventType"])
| summarize total = sum(value) by eventType
| order by total desc
```

## 6J. Stripe Webhook Processing Trace

Purpose:
See whether Stripe events were parsed, dispatched, skipped, or failed.

```kusto
traces
| where timestamp > ago(24h)
| extend operationName = tostring(customDimensions["OperationName"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend decision = tostring(customDimensions["Decision"])
| extend eventType = tostring(customDimensions["EventType"])
| extend stripeEventId = tostring(customDimensions["StripeEventId"])
| extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
| extend subscriptionId = tostring(customDimensions["SubscriptionId"])
| where operationName == "stripe_webhook"
    or message has "Stripe event"
    or message has "stripe_checkout_completed"
    or message has "stripe_subscription_reduce"
| project timestamp, operationName, eventType, stripeEventId, stripeCustomerId, subscriptionId, decision, outcome, reason, message, customDimensions
| order by timestamp desc
```

## 6K. Checkout And Subscription Handler Trace

Purpose:
Confirm whether checkout and subscription reducer handlers ran after payment.

If checkout ran but no access recompute happened, inspect the plan type and branch.

```kusto
traces
| where timestamp > ago(24h)
| extend operationName = tostring(customDimensions["OperationName"])
| extend step = tostring(customDimensions["Step"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend decision = tostring(customDimensions["Decision"])
| extend userId = tostring(customDimensions["UserId"])
| extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
| extend subscriptionId = tostring(customDimensions["SubscriptionId"])
| extend planType = tostring(customDimensions["PlanType"])
| extend mode = tostring(customDimensions["Mode"])
| extend source = tostring(customDimensions["Source"])
| where operationName in ("stripe_checkout_completed", "stripe_subscription_reduce", "access_recompute")
    or step in ("checkout_individual", "apply_signal_facts")
    or decision == "access_rules_decide"
| project timestamp, operationName, step, decision, outcome, reason, userId, stripeCustomerId, subscriptionId, planType, mode, source, message, customDimensions
| order by timestamp desc
```

## 6L. Stripe Customer Could Not Resolve To User

Purpose:
Find subscription or invoice events that could not sync because the backend could not resolve the Stripe customer to a HabloTruck user.

If this appears, check whether `checkout.session.completed` ran first and whether it created the Stripe customer lookup.

```kusto
traces
| where timestamp > ago(24h)
| extend decision = tostring(customDimensions["Decision"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend stripeCustomerId = tostring(customDimensions["StripeCustomerId"])
| extend subscriptionId = tostring(customDimensions["SubscriptionId"])
| where decision in ("user_resolution", "user_projection_missing")
    or reason in ("user_not_found_by_stripe_customer_id", "user_not_found_after_resolve")
| project timestamp, decision, outcome, reason, stripeCustomerId, subscriptionId, message, customDimensions
| order by timestamp desc
```

## 6M. Access Decisions After Payment

Purpose:
Confirm whether access was computed as `Full`, `Grace`, or `Blocked`.

```kusto
customMetrics
| where timestamp > ago(24h)
| where name == "access.decision"
| extend mode = tostring(customDimensions["mode"])
| extend source = tostring(customDimensions["source"])
| summarize total = sum(value) by mode, source
| order by total desc
```

## 6N. Drill Into One Stripe Webhook Operation

Purpose:
When webhook requests are visible but handler traces are not obvious, copy one `operation_Id` from query `6H` and inspect every telemetry item in that operation.

Replace `PASTE_OPERATION_ID_HERE`.

```kusto
let op = "PASTE_OPERATION_ID_HERE";
union isfuzzy=true
    (requests
     | where operation_Id == op
     | project timestamp, itemType = "request", name, message = "", resultCode, success, duration, customDimensions),
    (traces
     | where operation_Id == op
     | project timestamp, itemType = "trace", name = "", message, resultCode = "", success = bool(null), duration = timespan(null), customDimensions),
    (dependencies
     | where operation_Id == op
     | project timestamp, itemType = "dependency", name, message = target, resultCode, success, duration, customDimensions),
    (customMetrics
     | where operation_Id == op
     | project timestamp, itemType = "metric", name, message = strcat("value=", tostring(value)), resultCode = "", success = bool(null), duration = timespan(null), customDimensions),
    (exceptions
     | where operation_Id == op
     | project timestamp, itemType = "exception", name = type, message = outerMessage, resultCode = "", success = bool(null), duration = timespan(null), customDimensions)
| order by timestamp asc
```

## 6O. Broad Checkout/Access Search Without Custom Dimension Assumptions

Purpose:
Use this if structured custom dimensions such as `OperationName` are not showing up as expected.

```kusto
traces
| where timestamp > ago(24h)
| where message has_any (
    "stripe_checkout_completed",
    "stripe_subscription_reduce",
    "checkout_individual",
    "access_recompute",
    "access_rules_decide",
    "manychat_sync",
    "manychat_dispatch")
| project timestamp, message, operation_Id, customDimensions
| order by timestamp desc
```

## 6P. Stripe Webhook Final Outcomes

Purpose:
Show the final outcome/reason returned by the webhook request. This is useful when the request result is `200` but the event may have been skipped, duplicate, unhandled, or failed internally before sync.

```kusto
requests
| where timestamp > ago(24h)
| where name has "StripeWebhook" or url has "/api/stripe/webhook"
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| project timestamp, name, resultCode, success, duration, outcome, reason, operation_Id, customDimensions
| order by timestamp desc
```

## 6Q. Classify Stripe Webhooks By Storage Activity

Purpose:
When traces do not show the handler branch clearly, infer what happened from Azure Table dependencies.

Important signals:

- `HTUserStripeCustomer` 404 means the webhook could not resolve the Stripe customer to a HabloTruck user at that moment.
- `HTUsers` writes and `HTUserStripeCustomer` writes indicate checkout created/updated a user and Stripe customer lookup.
- `HTCompanies` queries/writes without user writes often indicate company/fleet projection or orphan subscription projection.
- No `manychat-dispatch` / no `manychat_sync` means access sync was never queued.

```kusto
let webhookRequests =
    requests
    | where timestamp > ago(24h)
    | where name has "StripeWebhook" or url has "/api/stripe/webhook"
    | project operation_Id, requestTime = timestamp, resultCode, success, duration;
let deps =
    dependencies
    | where timestamp > ago(24h)
    | where operation_Id in (webhookRequests)
    | extend dep = strcat(name, " | ", target, " | ", tostring(resultCode), " | success=", tostring(success));
webhookRequests
| join kind=leftouter (
    deps
    | summarize
        stripeCustomerLookup404 = countif((name has "HTUserStripeCustomer" or target has "HTUserStripeCustomer") and tostring(resultCode) == "404"),
        stripeCustomerLookupWrite = countif((name has "HTUserStripeCustomer" or target has "HTUserStripeCustomer") and name has_any ("POST", "PUT", "AddEntity", "UpdateEntity", "Upsert")),
        userTableWrite = countif((name has "HTUsers" or target has "HTUsers") and name has_any ("POST", "PUT", "AddEntity", "UpdateEntity", "Upsert")),
        companyTableTouched = countif(name has "HTCompanies" or target has "HTCompanies"),
        inviteTableTouched = countif(name has "HTInvite" or target has "HTInvite"),
        manyChatQueueTouched = countif(name has_any ("manychat-dispatch", "manychatdispatch") or target has_any ("manychat-dispatch", "manychatdispatch")),
        manyChatApiTouched = countif(target has "api.manychat.com" or name has "api.manychat.com" or target has "manychat" or name has "manychat"),
        dependencySample = make_set(dep, 20)
      by operation_Id
) on operation_Id
| project requestTime, operation_Id, resultCode, success, duration,
    stripeCustomerLookup404,
    stripeCustomerLookupWrite,
    userTableWrite,
    companyTableTouched,
    inviteTableTouched,
    manyChatQueueTouched,
    manyChatApiTouched,
    dependencySample
| order by requestTime desc
```

## 6R. Stripe Customer Lookup 404s By Operation

Purpose:
List webhook operations where the backend failed to find `HTUserStripeCustomer` rows.

```kusto
dependencies
| where timestamp > ago(24h)
| where name has "HTUserStripeCustomer" or target has "HTUserStripeCustomer"
| where tostring(resultCode) == "404"
| project timestamp, operation_Id, name, target, resultCode, success, customDimensions
| order by timestamp desc
```

## 6S. User/Stripe Lookup Writes

Purpose:
Confirm whether checkout ever created or updated user lookup rows.

```kusto
dependencies
| where timestamp > ago(24h)
| where name has_any ("HTUsers", "HTUserStripeCustomer", "HTUserManyChat", "HTUserEmail")
    or target has_any ("HTUsers", "HTUserStripeCustomer", "HTUserManyChat", "HTUserEmail")
| where name has_any ("POST", "PUT", "AddEntity", "UpdateEntity", "Upsert")
| project timestamp, operation_Id, name, target, resultCode, success, customDimensions
| order by timestamp desc
```

## 7. Failed Action Queue Volume

Purpose:
See which failed-action types are being queued most often.

```kusto
customMetrics
| where name == "failed.actions.queued"
| extend actionType = tostring(customDimensions["actionType"])
| summarize total = sum(value) by actionType
| order by total desc
```

## 8. Failed Action Retry Volume

Purpose:
See which failed-action types are being retried most often.

```kusto
customMetrics
| where name == "failed.actions.retried"
| extend actionType = tostring(customDimensions["actionType"])
| summarize total = sum(value) by actionType
| order by total desc
```

## 9. Failed Actions Queue vs Retry

Purpose:
Compare how much work is entering the failed-action system versus how much is being retried.

```kusto
customMetrics
| where name in ("failed.actions.queued", "failed.actions.retried")
| extend actionType = tostring(customDimensions["actionType"])
| summarize total = sum(value) by name, actionType
| order by total desc
```

## 10. Stripe Event Intake

Purpose:
See which Stripe events are arriving most often.

```kusto
customMetrics
| where name == "stripe.events.received"
| extend eventType = tostring(customDimensions["eventType"])
| summarize total = sum(value) by eventType
| order by total desc
```

## 11. Stripe Event Timeline

Purpose:
See Stripe webhook traffic over time.

```kusto
customMetrics
| where name == "stripe.events.received"
| summarize total = sum(value) by bin(timestamp, 15m)
| order by timestamp asc
```

## 12. Access Decision Distribution

Purpose:
See how access decisions are being applied by mode and source.

```kusto
customMetrics
| where name == "access.decision"
| extend mode = tostring(customDimensions["mode"])
| extend source = tostring(customDimensions["source"])
| summarize total = sum(value) by mode, source
| order by total desc
```

## 13. Access Decision Timeline

Purpose:
See access decision activity over time.

```kusto
customMetrics
| where name == "access.decision"
| extend mode = tostring(customDimensions["mode"])
| summarize total = sum(value) by bin(timestamp, 15m), mode
| order by timestamp asc
```

## 14. Exceptions In The Last Hour

Purpose:
Quick troubleshooting query for recent exceptions.

```kusto
exceptions
| where timestamp > ago(1h)
| project timestamp, problemId, outerType, outerMessage, operation_Name, cloud_RoleName
| order by timestamp desc
```

## 15. ManyChat-Related Exceptions

Purpose:
Focus on ManyChat dependency problems and queue fallback scenarios.

```kusto
traces
| where timestamp > ago(24h)
| where message has "manychat"
| project timestamp, severityLevel, message, operation_Name, customDimensions
| order by timestamp desc
```

## 16. Dependency Failures

Purpose:
See failed dependency calls, especially useful for Stripe or ManyChat troubleshooting.

```kusto
dependencies
| where timestamp > ago(24h)
| where success == false
| project timestamp, target, name, resultCode, duration, operation_Name
| order by timestamp desc
```

## 17. Slow Requests

Purpose:
Find slow endpoints and functions.

```kusto
requests
| where timestamp > ago(24h)
| project timestamp, name, resultCode, success, duration, operation_Name
| order by duration desc
```

## 18. Join-Specific Failures In Logs

Purpose:
Inspect recent `company/join` failures at the trace level.

```kusto
traces
| where timestamp > ago(24h)
| where message has "company_join"
| project timestamp, severityLevel, message, operation_Name, customDimensions
| order by timestamp desc
```

## 19. Quick Operational Dashboard Query

Purpose:
One compact summary for core custom metrics.

```kusto
customMetrics
| where name in (
    "company.join",
    "company.join.seat_refresh",
    "manychat.dispatch.queued",
    "manychat.dispatch.processed",
    "failed.actions.queued",
    "failed.actions.retried",
    "stripe.events.received",
    "subscription.plan_change",
    "company.seat_quantity_change",
    "access.decision")
| summarize total = sum(value) by name
| order by total desc
```

## 20. Subscription Plan Change Outcomes

Purpose:
Track individual upgrade/downgrade outcomes from `POST /api/stripe/subscription/change-plan`.

```kusto
customMetrics
| where timestamp > ago(24h)
| where name == "subscription.plan_change"
| extend outcome = tostring(customDimensions["outcome"])
| extend reason = tostring(customDimensions["reason"])
| extend targetPlanType = tostring(customDimensions["targetPlanType"])
| extend effectiveWhen = tostring(customDimensions["effectiveWhen"])
| summarize total = sum(value) by outcome, reason, targetPlanType, effectiveWhen
| order by total desc
```

## 21. Company Seat Quantity Change Outcomes

Purpose:
Track company/fleet seat quantity changes from `POST /api/stripe/subscription/update-seat-quantity`.

```kusto
customMetrics
| where timestamp > ago(24h)
| where name == "company.seat_quantity_change"
| extend outcome = tostring(customDimensions["outcome"])
| extend reason = tostring(customDimensions["reason"])
| extend direction = tostring(customDimensions["direction"])
| extend effectiveWhen = tostring(customDimensions["effectiveWhen"])
| summarize total = sum(value) by outcome, reason, direction, effectiveWhen
| order by total desc
```

## 22. Subscription Change Trace

Purpose:
Inspect logs for plan changes and seat quantity changes, including validation failures and Stripe dependency failures.

```kusto
traces
| where timestamp > ago(24h)
| extend operationName = tostring(customDimensions["OperationName"])
| extend outcome = tostring(customDimensions["Outcome"])
| extend reason = tostring(customDimensions["Reason"])
| extend userId = tostring(customDimensions["UserId"])
| extend companyId = tostring(customDimensions["CompanyId"])
| extend subscriptionId = tostring(customDimensions["SubscriptionId"])
| extend targetPlanType = tostring(customDimensions["TargetPlanType"])
| extend targetSeats = tostring(customDimensions["TargetSeats"])
| extend effectiveWhen = tostring(customDimensions["EffectiveWhen"])
| where operationName in (
    "subscription_plan_change",
    "subscription_plan_change_http",
    "company_seat_quantity_change",
    "company_seat_quantity_change_http")
| project timestamp, operationName, outcome, reason, userId, companyId, subscriptionId, targetPlanType, targetSeats, effectiveWhen, message, customDimensions
| order by timestamp desc
```

## Recommended Daily Checks

Purpose:
If you only run a few checks every day, start with these:

1. `Company Join Outcomes`
2. `Seat Refresh Usage`
3. `ManyChat Dispatch Processing Outcomes`
4. `Failed Action Queue Volume`
5. `Exceptions In The Last Hour`
6. `Slow Requests`

## What Good Looks Like

Healthy patterns usually look like this:

- `company.join` has mostly `completed`
- `company.join.seat_refresh` has a meaningful number of `refreshed=false`
- `manychat.dispatch.processed` is mostly `completed`
- `failed.actions.queued` stays low and stable
- exception volume is low
- request durations stay predictable

## What To Watch For

Investigate when you see:

- rising `seat_reservation_conflict`
- rising `manychat.dispatch.processed` with `moved_to_failed_actions`
- `failed.actions.queued` growing faster than `failed.actions.retried`
- sharp increases in `exceptions`
- slow growth in request duration around join or Stripe flows
