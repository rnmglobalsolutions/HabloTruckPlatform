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

    public async Task<StripeSubscriptionSnapshot?> ChangeSubscriptionPriceAsync(
        string subscriptionId,
        string targetPriceId,
        string prorationBehavior,
        string? billingCycleAnchor,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId) || string.IsNullOrWhiteSpace(targetPriceId))
            return null;

        var normalized = subscriptionId.Trim();
        var getWatch = Stopwatch.StartNew();
        var current = await _subscriptions.GetAsync(normalized, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "get_subscription_for_price_change",
            "Stripe API",
            getWatch.ElapsedMilliseconds,
            true,
            current is not null,
            normalized);

        var item = current?.Items?.Data?.FirstOrDefault();
        if (current is null || item is null || string.IsNullOrWhiteSpace(item.Id))
            return null;

        var options = BuildPriceChangeOptions(
            item.Id,
            targetPriceId.Trim(),
            prorationBehavior,
            billingCycleAnchor);

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        };

        var updateWatch = Stopwatch.StartNew();
        var updated = await _subscriptions.UpdateAsync(normalized, options, requestOptions, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId} TargetPriceId={TargetPriceId} ProrationBehavior={ProrationBehavior} BillingCycleAnchor={BillingCycleAnchor}",
            "dependency",
            "stripe",
            "update_subscription_price",
            "Stripe API",
            updateWatch.ElapsedMilliseconds,
            true,
            updated is not null,
            normalized,
            targetPriceId.Trim(),
            prorationBehavior,
            billingCycleAnchor);

        return updated is null ? null : MapSnapshot(updated);
    }

    public async Task<StripeSubscriptionSnapshot?> UpdateSubscriptionQuantityAsync(
        string subscriptionId,
        int targetQuantity,
        string prorationBehavior,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId) || targetQuantity <= 0)
            return null;

        var normalized = subscriptionId.Trim();
        var getWatch = Stopwatch.StartNew();
        var current = await _subscriptions.GetAsync(normalized, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "get_subscription_for_quantity_change",
            "Stripe API",
            getWatch.ElapsedMilliseconds,
            true,
            current is not null,
            normalized);

        var item = current?.Items?.Data?.FirstOrDefault();
        if (current is null || item is null || string.IsNullOrWhiteSpace(item.Id))
            return null;

        var options = BuildQuantityChangeOptions(item.Id, targetQuantity, prorationBehavior);

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        };

        var updateWatch = Stopwatch.StartNew();
        var updated = await _subscriptions.UpdateAsync(normalized, options, requestOptions, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId} TargetQuantity={TargetQuantity} ProrationBehavior={ProrationBehavior}",
            "dependency",
            "stripe",
            "update_subscription_quantity",
            "Stripe API",
            updateWatch.ElapsedMilliseconds,
            true,
            updated is not null,
            normalized,
            targetQuantity,
            prorationBehavior);

        return updated is null ? null : MapSnapshot(updated);
    }

    internal static SubscriptionUpdateOptions BuildPriceChangeOptions(
        string subscriptionItemId,
        string targetPriceId,
        string prorationBehavior,
        string? billingCycleAnchor)
    {
        var options = new SubscriptionUpdateOptions
        {
            ProrationBehavior = string.IsNullOrWhiteSpace(prorationBehavior) ? null : prorationBehavior.Trim(),
            PaymentBehavior = ResolvePaymentBehavior(prorationBehavior),
            Items =
            [
                new SubscriptionItemOptions
                {
                    Id = subscriptionItemId,
                    Price = targetPriceId
                }
            ]
        };

        options.BillingCycleAnchor = NormalizeBillingCycleAnchor(billingCycleAnchor);

        return options;
    }

    internal static SubscriptionUpdateOptions BuildQuantityChangeOptions(
        string subscriptionItemId,
        int targetQuantity,
        string prorationBehavior)
        => new()
        {
            ProrationBehavior = string.IsNullOrWhiteSpace(prorationBehavior) ? null : prorationBehavior.Trim(),
            PaymentBehavior = ResolvePaymentBehavior(prorationBehavior),
            Items =
            [
                new SubscriptionItemOptions
                {
                    Id = subscriptionItemId,
                    Quantity = targetQuantity
                }
            ]
        };

    private static string? ResolvePaymentBehavior(string? prorationBehavior)
        => string.Equals(prorationBehavior?.Trim(), "always_invoice", StringComparison.OrdinalIgnoreCase)
            ? "error_if_incomplete"
            : null;

    private static SubscriptionBillingCycleAnchor? NormalizeBillingCycleAnchor(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized switch
        {
            null or "" => null,
            "now" => SubscriptionBillingCycleAnchor.Now,
            "unchanged" => SubscriptionBillingCycleAnchor.Unchanged,
            _ => null
        };
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
            Quantity: item?.Quantity is long quantity ? checked((int)quantity) : null,
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
