using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IStripeAdminClient
{
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default);
    Task<StripeEventData?> GetEventDataAsync(
        string eventId, CancellationToken ct = default);
}