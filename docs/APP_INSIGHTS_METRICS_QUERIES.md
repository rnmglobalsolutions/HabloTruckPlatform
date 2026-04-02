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
    "access.decision")
| summarize total = sum(value) by name
| order by total desc
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
