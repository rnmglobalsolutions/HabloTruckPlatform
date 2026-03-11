using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeSubscriptionGateway : IStripeSubscriptionGateway
{
    private readonly SubscriptionService _subscriptions;
    private readonly ILogger<StripeSubscriptionGateway> _logger;

    public StripeSubscriptionGateway(ILogger<StripeSubscriptionGateway>? logger = null)
    {
        _subscriptions = new SubscriptionService();
        _logger = logger ?? NullLogger<StripeSubscriptionGateway>.Instance;
    }

    public async Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return null;

        var normalized = subscriptionId.Trim();
        var watch = Stopwatch.StartNew();

        var sub = await _subscriptions.GetAsync(normalized, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "get_subscription",
            "Stripe API",
            watch.ElapsedMilliseconds,
            true,
            sub is not null,
            normalized);

        if (sub is null)
            return null;

        return MapSnapshot(sub);
    }

    public async Task<StripeSubscriptionSnapshot?> ScheduleCancelAtPeriodEndAsync(
        string subscriptionId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            return null;

        var options = new SubscriptionUpdateOptions
        {
            CancelAtPeriodEnd = true
        };

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        };

        var normalized = subscriptionId.Trim();
        var watch = Stopwatch.StartNew();

        var sub = await _subscriptions.UpdateAsync(normalized, options, requestOptions, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "update_subscription_cancel_at_period_end",
            "Stripe API",
            watch.ElapsedMilliseconds,
            true,
            sub is not null,
            normalized);

        if (sub is null)
            return null;

        return MapSnapshot(sub);
    }

    internal static StripeSubscriptionSnapshot MapSnapshot(Subscription sub)
    {
        var item = sub.Items?.Data?.FirstOrDefault();

        return new StripeSubscriptionSnapshot(
            SubscriptionId: sub.Id,
            CustomerId: sub.CustomerId,
            Status: sub.Status,
            PriceId: item?.Price?.Id,
            Interval: item?.Price?.Recurring?.Interval,
            CancelAtPeriodEnd: sub.CancelAtPeriodEnd,
            CurrentPeriodEndUtc: ComputeEffectivePeriodEndUtc(sub.Items?.Data),
            CanceledAtUtc: ToDateTimeOffsetUtc(sub.CanceledAt),
            EndedAtUtc: ToDateTimeOffsetUtc(sub.EndedAt)
        );
    }

    internal static DateTimeOffset? ComputeEffectivePeriodEndUtc(IEnumerable<SubscriptionItem>? items)
    {
        DateTimeOffset? max = null;

        foreach (var item in items ?? Enumerable.Empty<SubscriptionItem>())
        {
            var current = ToDateTimeOffsetUtc(item.CurrentPeriodEnd);
            if (current is null)
                continue;

            if (max is null || current > max)
                max = current;
        }

        return max;
    }

    internal static DateTimeOffset? ToDateTimeOffsetUtc(DateTime? value)
    {
        if (value is null || value.Value == default)
            return null;

        var dt = value.Value;

        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        return new DateTimeOffset(dt).ToUniversalTime();
    }

    internal static DateTimeOffset? ToDateTimeOffsetUtc(DateTime value)
        => value == default ? null : ToDateTimeOffsetUtc((DateTime?)value);
}

