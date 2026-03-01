using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace HabloTruckPlatform.Infrastructure.Telemetry;

public sealed class Metrics
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
}