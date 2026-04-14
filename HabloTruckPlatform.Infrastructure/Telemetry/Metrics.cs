using HabloTruckPlatform.Application.Abstractions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace HabloTruckPlatform.Infrastructure.Telemetry;

public sealed class Metrics : IAppMetrics
{
    private readonly TelemetryClient _telemetry;

    public Metrics(TelemetryClient telemetry)
    {
        _telemetry = telemetry;
    }

    public void StripeEventReceived(string eventType)
    {
        _telemetry.GetMetric("stripe.events.received", "eventType").TrackValue(1, eventType);
    }

    public void AccessDecisionApplied(string mode, string source)
    {
        _telemetry.GetMetric("access.decision", "mode", "source")
                  .TrackValue(1, mode, source);
    }

    public void FailedActionQueued(string actionType)
    {
        _telemetry.GetMetric("failed.actions.queued", "actionType")
                  .TrackValue(1, actionType);
    }

    public void FailedActionRetried(string actionType)
    {
        _telemetry.GetMetric("failed.actions.retried", "actionType")
                  .TrackValue(1, actionType);
    }

    public void CompanyJoin(string outcome, string reason)
    {
        _telemetry.GetMetric("company.join", "outcome", "reason")
                  .TrackValue(1, outcome, reason);
    }

    public void CompanyJoinSeatRefresh(bool refreshed, string reason)
    {
        _telemetry.GetMetric("company.join.seat_refresh", "refreshed", "reason")
                  .TrackValue(1, refreshed ? "true" : "false", reason);
    }

    public void ManyChatDispatchQueued(string actionType)
    {
        _telemetry.GetMetric("manychat.dispatch.queued", "actionType")
                  .TrackValue(1, actionType);
    }

    public void ManyChatDispatchProcessed(string actionType, string outcome)
    {
        _telemetry.GetMetric("manychat.dispatch.processed", "actionType", "outcome")
                  .TrackValue(1, actionType, outcome);
    }

    public void SubscriptionPlanChange(string outcome, string reason, string targetPlanType, string effectiveWhen)
    {
        _telemetry.GetMetric("subscription.plan_change", "outcome", "reason", "targetPlanType", "effectiveWhen")
                  .TrackValue(1, outcome, reason, targetPlanType, effectiveWhen);
    }

    public void CompanySeatQuantityChange(string outcome, string reason, string direction, string effectiveWhen)
    {
        _telemetry.GetMetric("company.seat_quantity_change", "outcome", "reason", "direction", "effectiveWhen")
                  .TrackValue(1, outcome, reason, direction, effectiveWhen);
    }
}
