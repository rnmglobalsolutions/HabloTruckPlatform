# Azure Alerts Setup Guide

This guide explains how to configure the main Azure alerts for HabloTruck and what the operating procedure should be once each alert fires.

It is written for the current platform shape:

- Azure Functions
- Application Insights
- Log Analytics / KQL-based troubleshooting
- ManyChat asynchronous dispatch
- Stripe webhook-driven subscription projection

This guide is intentionally practical:

- what to create
- where to click
- what query to use
- what threshold to start with
- what the on-call or operator should do next

## Purpose

The goal is to give HabloTruck a small but effective alerting baseline so that the team can detect:

- application errors
- ManyChat delivery problems
- failed-action backlog growth
- subscription webhook issues
- join-seat contention or business friction
- slow or unhealthy API behavior

## Recommended Alerting Strategy

Start with two categories:

1. **Platform health alerts**
   - exceptions
   - failed dependencies
   - slow requests

2. **Business and integration alerts**
   - ManyChat dispatch fallback
   - failed-action backlog growth
   - `company/join` conflict spikes
   - Stripe webhook anomaly

## Prerequisites

Before creating the alert rules, make sure you already have:

- an `Application Insights` resource
- a `Log Analytics` workspace linked to it
- the custom telemetry described in [APP_INSIGHTS_METRICS_QUERIES.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/docs/APP_INSIGHTS_METRICS_QUERIES.md)
- one or more people who should receive the notifications

## Step 1: Create an Action Group

You only need to do this once per environment.

### In Azure Portal

1. Open `Azure Monitor`.
2. Go to `Alerts`.
3. Click `Manage actions`.
4. Click `Create`.
5. Choose the correct subscription and resource group.
6. Set:
   - `Action group name`: `hablotruck-ops`
   - `Display name`: `HabloTruck Ops`
7. Add at least one notification channel:
   - email
   - SMS
   - Teams / webhook if available in your organization
8. Save the action group.

### Recommendation

Create at least:

- one email action to the technical owner
- one shared email or team channel for visibility

## Step 2: Choose the Right Alert Type

For HabloTruck, most of the recommended alerts should be created as:

- **Scheduled query alerts**

Why:

- they work well with `Application Insights > Logs`
- they let you alert on custom metrics and KQL conditions

Use them for:

- exceptions
- ManyChat queue fallbacks
- failed-action queue growth
- business conflicts
- Stripe event anomalies

## Step 3: Generic Procedure for Creating a Scheduled Query Alert

Use this pattern for each alert below.

### In Azure Portal

1. Open `Azure Monitor`.
2. Go to `Alerts`.
3. Click `Create`.
4. Click `Alert rule`.
5. Under `Scope`, select your `Application Insights` resource.
6. Under `Condition`, choose `Custom log search`.
7. Paste the KQL query.
8. Set evaluation settings:
   - `Check every`: usually `5 minutes`
   - `Lookback period`: usually `15 minutes`
9. Configure alert logic:
   - `Table rows > 0`
   - or another threshold if specified below
10. Under `Actions`, attach the `hablotruck-ops` action group.
11. Under `Details`, define:
   - alert rule name
   - severity
   - description
12. Create the alert.

## Severity Guide

Recommended severity model:

- `Sev 0`: immediate outage or production-wide critical failure
- `Sev 1`: critical customer-impacting issue
- `Sev 2`: high-priority operational issue
- `Sev 3`: warning or business friction worth investigating
- `Sev 4`: informational trend signal

## Alert 1: Exceptions Detected

### Purpose

Catch application exceptions quickly.

### Query

```kusto
exceptions
| where timestamp > ago(15m)
```

### Initial Threshold

- trigger if `table rows > 0`

### Recommended Settings

- check every: `5 minutes`
- lookback: `15 minutes`
- severity: `Sev 1`
- alert name: `hablotruck-exceptions-detected`

### What to do when it fires

1. Open the alert details.
2. Go to `Search results` or run the same query in Logs.
3. Review:
   - exception type
   - message
   - affected function
   - whether it is repeating
4. Check `requests` and `dependencies` around the same time.
5. Decide:
   - one-off transient issue
   - deployment regression
   - configuration problem

### Response Procedure

- If it is a new deployment issue:
  - stop promoting the build
  - inspect the last deployed changes
- If it affects a critical endpoint:
  - test the endpoint manually
  - consider hotfix or rollback
- If it is transient and isolated:
  - monitor for recurrence

## Alert 2: ManyChat Dispatch Falling Back to Failed Actions

### Purpose

Detect when queued ManyChat work is no longer being delivered successfully and is being moved into failed-action storage.

### Query

```kusto
customMetrics
| where timestamp > ago(15m)
| where name == "manychat.dispatch.processed"
| extend outcome = tostring(customDimensions["outcome"])
| where outcome == "moved_to_failed_actions"
```

### Initial Threshold

- trigger if `table rows >= 5`

### Recommended Settings

- check every: `5 minutes`
- lookback: `15 minutes`
- severity: `Sev 2`
- alert name: `hablotruck-manychat-dispatch-fallback-spike`

### What to do when it fires

1. Run the detailed ManyChat queries from [APP_INSIGHTS_METRICS_QUERIES.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/docs/APP_INSIGHTS_METRICS_QUERIES.md).
2. Identify:
   - which `actionType` is failing
   - whether failures are retryable
   - whether the issue is localized or widespread
3. Check:
   - ManyChat API status
   - API key validity
   - recent deployments
   - outbound networking issues

### Response Procedure

- If it is a ManyChat outage or transient API issue:
  - let retries continue
  - monitor whether `failed.actions.retried` catches up
- If it is a configuration issue:
  - verify `ManyChat__ApiKey`
  - verify flow namespaces
  - verify field/tag names
- If the backlog keeps growing:
  - inspect `admin/failed-actions`
  - requeue only after fixing root cause

## Alert 3: Failed Action Queue Growth

### Purpose

Detect whether failed actions are accumulating faster than the retry pipeline can process them.

### Query

```kusto
let queued =
customMetrics
| where timestamp > ago(30m)
| where name == "failed.actions.queued"
| summarize queuedTotal = sum(value);
let retried =
customMetrics
| where timestamp > ago(30m)
| where name == "failed.actions.retried"
| summarize retriedTotal = sum(value);
queued
| join kind=fullouter retried on 1 == 1
| extend queuedTotal = coalesce(queuedTotal, 0.0)
| extend retriedTotal = coalesce(retriedTotal, 0.0)
| extend delta = queuedTotal - retriedTotal
| where delta > 20
```

### Initial Threshold

- trigger if `table rows > 0`

### Recommended Settings

- check every: `10 minutes`
- lookback: `30 minutes`
- severity: `Sev 2`
- alert name: `hablotruck-failed-actions-backlog-growth`

### What to do when it fires

1. Run:
   - failed actions queued query
   - failed actions retried query
2. Identify the dominant `actionType`.
3. Check whether the failing dependency is:
   - ManyChat
   - Stripe
   - storage
4. Inspect the admin failed-actions endpoints if available.

### Response Procedure

- If retry throughput is simply behind:
  - monitor for natural recovery
- If one action type is broken:
  - fix root cause
  - then requeue
- If dead-letter style accumulation appears:
  - manually inspect a few payloads before bulk requeueing

## Alert 4: Company Join Conflict Spike

### Purpose

Detect increased friction in the `company/join` flow, especially around seat reservation conflicts or no-capacity events.

### Query

```kusto
customMetrics
| where timestamp > ago(15m)
| where name == "company.join"
| extend outcome = tostring(customDimensions["outcome"])
| extend reason = tostring(customDimensions["reason"])
| where outcome == "denied"
| where reason in ("seat_reservation_conflict", "seat_assignment_conflict", "no_seats_available", "company_over_capacity")
```

### Initial Threshold

- trigger if `table rows >= 10`

### Recommended Settings

- check every: `5 minutes`
- lookback: `15 minutes`
- severity: `Sev 3`
- alert name: `hablotruck-company-join-conflict-spike`

### What to do when it fires

1. Determine which reason is dominant.
2. If `no_seats_available` or `company_over_capacity`:
   - this may be a business capacity issue, not a platform failure
3. If `seat_reservation_conflict` or `seat_assignment_conflict`:
   - investigate concurrency hotspot behavior

### Response Procedure

- For capacity issues:
  - verify company entitlement and invite remaining count
  - inform the customer if the pack is exhausted
- For conflict spikes:
  - check whether a large onboarding is happening
  - monitor whether conflicts settle naturally
  - if not, investigate storage contention and queue pressure

## Alert 5: Slow Requests

### Purpose

Detect degraded endpoint performance before it becomes an outage.

### Query

```kusto
requests
| where timestamp > ago(15m)
| where duration > 5s
```

### Initial Threshold

- trigger if `table rows >= 10`

### Recommended Settings

- check every: `5 minutes`
- lookback: `15 minutes`
- severity: `Sev 2`
- alert name: `hablotruck-slow-requests`

### What to do when it fires

1. Check which endpoints are slow.
2. Correlate with:
   - dependency failures
   - Stripe latency
   - ManyChat dispatch issues
   - storage contention
3. Determine whether the issue is limited to:
   - one function
   - one dependency
   - the whole app

### Response Procedure

- If only one endpoint is slow:
  - inspect that path specifically
- If all requests are slower:
  - inspect infrastructure load and recent deployments
- If tied to one dependency:
  - treat it as a downstream service issue

## Alert 6: Stripe Webhook Silence or Anomaly

### Purpose

Detect when Stripe events stop arriving unexpectedly.

### Important Note

This alert is useful only if you expect regular Stripe traffic.
If your volume is low or bursty, treat this as optional.

### Query

```kusto
customMetrics
| where timestamp > ago(60m)
| where name == "stripe.events.received"
| summarize total = sum(value)
| where total == 0
```

### Initial Threshold

- trigger if `table rows > 0`

### Recommended Settings

- check every: `15 minutes`
- lookback: `60 minutes`
- severity: `Sev 3`
- alert name: `hablotruck-stripe-webhook-silence`

### What to do when it fires

1. Confirm whether traffic was actually expected in that period.
2. Check Stripe dashboard:
   - webhook deliveries
   - failed webhook attempts
3. Check app logs for webhook exceptions.
4. Check if there was a deployment or configuration change.

### Response Procedure

- If no Stripe traffic was expected:
  - close as informational
- If traffic was expected:
  - inspect Stripe webhook endpoint health
  - inspect webhook secret and routing
  - manually replay missed events if needed

## Alert 7: ManyChat Billing Action Required Active

### Purpose

Detect recurring billing-recovery pressure in the user base.

### Query

```kusto
customMetrics
| where timestamp > ago(30m)
| where name == "manychat.dispatch.queued"
| extend actionType = tostring(customDimensions["actionType"])
| where actionType in ("manychat.payment_failed_flow", "manychat.billing_recovery_state", "manychat.subscription_reminder")
```

### Initial Threshold

- trigger if `table rows >= 20`

### Recommended Settings

- check every: `10 minutes`
- lookback: `30 minutes`
- severity: `Sev 3`
- alert name: `hablotruck-billing-recovery-pressure`

### What to do when it fires

1. Determine whether this is a real payment-failure spike or just normal billing-cycle timing.
2. Check Stripe invoice and subscription event mix.
3. Review:
   - `payment failed`
   - `payment recovery`
   - reminder flows

### Response Procedure

- If it aligns with billing-cycle timing and resolves:
  - monitor only
- If it is larger than expected:
  - inspect Stripe-side payment failures
  - verify payment update flows are working

## Alert 8: Seat Refresh Spike

### Purpose

Detect when `company/join` frequently needs to refresh `SeatsUsed`, which can indicate stress or stale entitlement usage patterns.

### Query

```kusto
customMetrics
| where timestamp > ago(30m)
| where name == "company.join.seat_refresh"
| extend refreshed = tostring(customDimensions["refreshed"])
| where refreshed == "true"
```

### Initial Threshold

- trigger if `table rows >= 20`

### Recommended Settings

- check every: `10 minutes`
- lookback: `30 minutes`
- severity: `Sev 4`
- alert name: `hablotruck-seat-refresh-spike`

### What to do when it fires

1. Check whether a large onboarding is happening.
2. Compare with `company.join` denied reasons.
3. Decide whether this is:
   - normal burst behavior
   - stale seat usage drift
   - early sign of contention

### Response Procedure

- Usually monitor first.
- If combined with join conflicts, escalate investigation.

## Alert 9: Dependency Failures

### Purpose

Catch Stripe or ManyChat dependency failures directly.

### Query

```kusto
dependencies
| where timestamp > ago(15m)
| where success == false
```

### Initial Threshold

- trigger if `table rows >= 5`

### Recommended Settings

- check every: `5 minutes`
- lookback: `15 minutes`
- severity: `Sev 2`
- alert name: `hablotruck-dependency-failures`

### What to do when it fires

1. Identify the failing target:
   - Stripe
   - ManyChat
   - storage
2. Check whether it is:
   - authentication/configuration
   - service degradation
   - timeout
3. Compare with request failures and exceptions.

### Response Procedure

- If only one dependency is failing:
  - focus there first
- If multiple dependencies fail:
  - inspect broader infrastructure or networking issues

## Optional Alert 10: Access Decision Anomaly

### Purpose

Detect unexpected changes in access-mode outcomes.

### Query

```kusto
customMetrics
| where timestamp > ago(30m)
| where name == "access.decision"
| extend mode = tostring(customDimensions["mode"])
| summarize total = sum(value) by mode
```

### Recommendation

Do not create this as an alert immediately unless you already know what “normal” looks like.
Use it first as a dashboard signal.

## Recommended Initial Alert Set

If you want the smallest useful set, start with these six:

1. `hablotruck-exceptions-detected`
2. `hablotruck-manychat-dispatch-fallback-spike`
3. `hablotruck-failed-actions-backlog-growth`
4. `hablotruck-slow-requests`
5. `hablotruck-dependency-failures`
6. `hablotruck-company-join-conflict-spike`

Then add:

7. `hablotruck-stripe-webhook-silence`
8. `hablotruck-seat-refresh-spike`

## Triage Workflow

Use this triage sequence whenever an alert fires:

1. Confirm the alert is real and recent.
2. Determine whether it is:
   - platform issue
   - dependency issue
   - business-capacity issue
3. Check related dashboards and logs.
4. Identify:
   - customer impact
   - scope
   - whether the issue is still active
5. Decide:
   - monitor
   - fix config
   - replay/retry
   - hotfix
   - rollback

## Runbook Checklist by Category

### If the issue is ManyChat-related

- check ManyChat API status
- check `manychat.dispatch.processed`
- check `failed.actions.queued`
- verify API key and flow namespaces
- requeue only after root cause is fixed

### If the issue is Stripe-related

- check Stripe dashboard
- check webhook delivery status
- inspect recent `stripe.events.received`
- inspect dependency failures to Stripe
- replay failed events if needed

### If the issue is join/capacity-related

- inspect `company.join` reasons
- inspect invite remaining count
- inspect entitlement seat usage
- determine whether it is conflict or real capacity exhaustion

### If the issue is platform-related

- inspect exceptions
- inspect slow requests
- inspect dependency failures
- compare with last deployment

## Suggested Review Cadence

### Every day

- exceptions
- dependency failures
- ManyChat dispatch processed outcomes
- failed-action queue growth

### Every week

- company join outcomes
- seat refresh behavior
- Stripe webhook volume
- request latency trends

## Related Documents

- [APP_INSIGHTS_METRICS_QUERIES.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/docs/APP_INSIGHTS_METRICS_QUERIES.md)
- [MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md)
- [HabloTruck_Endpoints_Postman_Guide.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/docs/HabloTruck_Endpoints_Postman_Guide.md)

## Final Recommendation

Do not try to create every possible alert on day one.

Start with:

- exceptions
- dependency failures
- ManyChat fallback
- failed-action backlog
- slow requests
- join conflicts

Then expand once you learn the real operating patterns of the system in production.
