using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using Stripe;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeAdminClient : IStripeAdminClient
{
    private readonly IStripeSubscriptionGateway _subscriptions;

    public StripeAdminClient(IStripeSubscriptionGateway subscriptions)
    {
        _subscriptions = subscriptions;
    }

    public async Task<StripeEventData?> GetEventDataAsync(
        string eventId, CancellationToken ct = default)
    {
        var eventService = new EventService();
        var stripeEvent = await eventService.GetAsync(eventId, cancellationToken: ct);
        if (stripeEvent is null) return null;

        var parser = new StripeEventParser();
        var parsed = parser.Parse(stripeEvent);

        return parsed.Data;
    }

    public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default)
        => _subscriptions.GetSubscriptionAsync(subscriptionId, ct);
}
