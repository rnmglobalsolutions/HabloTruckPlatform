using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeAdminClient : IStripeAdminClient
{
    private readonly IStripeSubscriptionGateway _subscriptions;
    private readonly ILogger<StripeAdminClient> _logger;

    public StripeAdminClient(
        IStripeSubscriptionGateway subscriptions,
        ILogger<StripeAdminClient>? logger = null)
    {
        _subscriptions = subscriptions;
        _logger = logger ?? NullLogger<StripeAdminClient>.Instance;
    }

    public async Task<StripeEventData?> GetEventDataAsync(
        string eventId, CancellationToken ct = default)
    {
        var eventService = new EventService();
        var watch = Stopwatch.StartNew();

        var stripeEvent = await eventService.GetAsync(eventId, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} StripeEventId={StripeEventId}",
            "dependency",
            "stripe",
            "get_event",
            "Stripe API",
            watch.ElapsedMilliseconds,
            true,
            stripeEvent is not null,
            eventId);

        if (stripeEvent is null)
            return null;

        var parser = new StripeEventParser();
        var parsed = parser.Parse(stripeEvent);

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} EventType={EventType}",
            "step",
            "parse_event",
            parsed.Data is null ? "no_action_needed" : "applied",
            parsed.EventType);

        return parsed.Data;
    }

    public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default)
        => _subscriptions.GetSubscriptionAsync(subscriptionId, ct);
}

