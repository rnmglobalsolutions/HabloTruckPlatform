using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IManyChatSync
{
    /// <summary>
    /// Apply tags/fields based on the effective access decision.
    /// Should be best-effort; failures go to FailedAction store.
    /// </summary>
    Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default);

    /// <summary>
    /// Optional: trigger a "payment failed" recovery flow.
    /// </summary>
    Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default);

    /// <summary>
    /// Optional: notify company admin that a pack was purchased.
    /// </summary>
    Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default);

    /// <summary>
    /// Optional: trigger subscription renewal / churn-prevention reminders.
    /// </summary>
    Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default);

    /// <summary>
    /// Optional: sync payment recovery progress fields/tags.
    /// </summary>
    Task SyncBillingRecoveryStatusAsync(BillingRecoveryManyChatUpdate update, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Optional: remove tag by name
    /// </summary>
    Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default);

    /// <summary>
    /// Optional: add tag by name
    /// </summary>
    Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default);

    /// <summary>
    /// Optional: set custom field by name
    /// </summary>
    Task<ManyChatResponse> SetCustomFieldByNameAsync(
        string subscriberId, string fieldName, string value, CancellationToken ct = default);
}
