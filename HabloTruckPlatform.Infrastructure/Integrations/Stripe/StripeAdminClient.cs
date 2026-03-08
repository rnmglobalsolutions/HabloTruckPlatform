using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using Newtonsoft.Json.Linq;
using Stripe;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeAdminClient : IStripeAdminClient
{
    private readonly SubscriptionService _subscriptionService;

    public StripeAdminClient()
    {
        _subscriptionService = new SubscriptionService();
    }

    public async Task<StripeEventData?> GetEventDataAsync(
        string eventId, CancellationToken ct = default)
    {
        var eventService = new EventService();
        var stripeEvent = await eventService.GetAsync(eventId, cancellationToken: ct);
        if (stripeEvent is null) return null;

        var parser = new HabloTruckPlatform.Application.Integrations.Stripex.StripeEventParser();
        var parsed = parser.Parse(stripeEvent);

        return parsed.Data;
    }

    public async Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return null;

        var sub = await _subscriptionService.GetAsync(subscriptionId, cancellationToken: ct);
        if (sub is null)
            return null;

        var item = sub.Items?.Data?.FirstOrDefault();
        var raw = sub.RawJObject;

        return new StripeSubscriptionSnapshot(
            SubscriptionId: sub.Id,
            CustomerId: sub.CustomerId,
            Status: sub.Status,
            PriceId: item?.Price?.Id ?? raw?["items"]?["data"]?.First?["price"]?["id"]?.ToString(),
            Interval: item?.Price?.Recurring?.Interval ?? raw?["items"]?["data"]?.First?["price"]?["recurring"]?["interval"]?.ToString(),
            CancelAtPeriodEnd: raw?["cancel_at_period_end"]?.Value<bool?>() ?? false,
            CurrentPeriodEndUtc: ToDateTimeOffsetUtc(raw?["current_period_end"]),
            CanceledAtUtc: ToDateTimeOffsetUtc(raw?["canceled_at"]),
            EndedAtUtc: ToDateTimeOffsetUtc(raw?["ended_at"])
        );
    }

    private static DateTimeOffset? ToDateTimeOffsetUtc(JToken? token)
    {
        if (token is null)
            return null;

        if ((token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            && long.TryParse(token.ToString().Split('.')[0], out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds).ToUniversalTime();
        }

        if (DateTimeOffset.TryParse(token.ToString(), out var dto))
            return dto.ToUniversalTime();

        return null;
    }
}