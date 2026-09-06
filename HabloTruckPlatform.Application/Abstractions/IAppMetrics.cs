namespace HabloTruckPlatform.Application.Abstractions;

public interface IAppMetrics
{
    void StripeEventReceived(string eventType);
    void AccessDecisionApplied(string mode, string source);
    void FailedActionQueued(string actionType);
    void FailedActionRetried(string actionType);
    void CompanyJoin(string outcome, string reason);
    void CompanyJoinSeatRefresh(bool refreshed, string reason);
    void ManyChatDispatchQueued(string actionType);
    void ManyChatDispatchProcessed(string actionType, string outcome);
    void SubscriptionPlanChange(string outcome, string reason, string targetPlanType, string effectiveWhen);
    void CompanySeatQuantityChange(string outcome, string reason, string direction, string effectiveWhen);
}
