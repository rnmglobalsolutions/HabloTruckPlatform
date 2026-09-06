using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class UpdateCompanySeatQuantityUseCase
{
    private const string OperationName = "company_seat_quantity_change";
    private const string EffectiveImmediate = "immediate";
    private const string EffectiveNextInvoice = "next_invoice";

    private readonly IUserStore _users;
    private readonly ICompanyStore _companies;
    private readonly IEntitlementStore _entitlements;
    private readonly IStripeSubscriptionGateway _stripeSubscriptions;
    private readonly IClock _clock;
    private readonly IAppMetrics? _metrics;
    private readonly IAdminPaymentAlertNotifier? _adminPaymentAlerts;
    private readonly ILogger<UpdateCompanySeatQuantityUseCase> _logger;

    public UpdateCompanySeatQuantityUseCase(
        IUserStore users,
        ICompanyStore companies,
        IEntitlementStore entitlements,
        IStripeSubscriptionGateway stripeSubscriptions,
        IClock clock,
        ILogger<UpdateCompanySeatQuantityUseCase>? logger = null,
        IAppMetrics? metrics = null,
        IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        _users = users;
        _companies = companies;
        _entitlements = entitlements;
        _stripeSubscriptions = stripeSubscriptions;
        _clock = clock;
        _logger = logger ?? NullLogger<UpdateCompanySeatQuantityUseCase>.Instance;
        _metrics = metrics;
        _adminPaymentAlerts = adminPaymentAlerts;
    }

    public async Task<UpdateCompanySeatQuantityResult> ExecuteAsync(
        UpdateCompanySeatQuantityRequest request,
        CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();
        var nowUtc = _clock.UtcNow;
        var requestedEffectiveWhen = NormalizeEffectiveWhen(request?.EffectiveWhen);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = OperationName,
            ["UserId"] = request?.ActorUserId,
            ["CompanyId"] = request?.CompanyId,
            ["SubscriptionId"] = request?.SubscriptionId,
            ["TargetSeats"] = request?.TargetSeats,
            ["EffectiveWhen"] = requestedEffectiveWhen
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} CompanyId={CompanyId} TargetSeats={TargetSeats} EffectiveWhen={EffectiveWhen}",
            "entry",
            OperationName,
            request?.CompanyId,
            request?.TargetSeats,
            requestedEffectiveWhen);

        if (request is null)
            return Fail("invalid_request", opWatch, nowUtc, request?.CompanyId, null, null, 0, 0, 0, "-", requestedEffectiveWhen);

        if (request.TargetSeats <= 0)
            return Fail("target_seats_must_be_greater_than_zero", opWatch, nowUtc, request.CompanyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var actorPk = NullIfBlank(request.ActorUserPk);
        var actorId = NullIfBlank(request.ActorUserId);
        if (actorPk is null || actorId is null)
            return Fail("actor_required", opWatch, nowUtc, request.CompanyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var companyId = NullIfBlank(request.CompanyId);
        if (companyId is null)
            return Fail("company_id_required", opWatch, nowUtc, request.CompanyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var actorWatch = Stopwatch.StartNew();
        var actor = await _users.GetAsync(actorPk, actorId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "user.get",
            "Users",
            actorWatch.ElapsedMilliseconds,
            actor is not null);

        if (actor is null)
            return Fail("actor_not_found", opWatch, nowUtc, companyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var companyWatch = Stopwatch.StartNew();
        var company = await _companies.GetAsync(companyId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "company.get",
            "Companies",
            companyWatch.ElapsedMilliseconds,
            company is not null);

        if (company is null)
            return Fail("company_not_found", opWatch, nowUtc, companyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        if (!IsAuthorizedCompanyActor(actor, company))
            return Fail("forbidden", opWatch, nowUtc, companyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var subscriptionId = NullIfBlank(request.SubscriptionId) ?? TryGetSubscriptionIdFromEntitlementId(actor.SeatEntitlementId);
        if (subscriptionId is null)
            return Fail("subscription_id_required", opWatch, nowUtc, companyId, null, request.SubscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var entitlementId = ResolveEntitlementIdForFleetSubscription(subscriptionId);
        var entitlementWatch = Stopwatch.StartNew();
        var entitlement = await _entitlements.GetAsync(companyId, entitlementId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found} EntitlementId={EntitlementId}",
            "persistence",
            "entitlement.get",
            "Entitlements",
            entitlementWatch.ElapsedMilliseconds,
            entitlement is not null,
            entitlementId);

        if (entitlement is null)
            return Fail("company_entitlement_not_found", opWatch, nowUtc, companyId, entitlementId, subscriptionId, 0, request.TargetSeats, 0, "-", requestedEffectiveWhen);

        var direction = request.TargetSeats > entitlement.SeatsTotal
            ? "increase"
            : request.TargetSeats < entitlement.SeatsTotal
                ? "decrease"
                : "same";

        if (direction == "same")
        {
            if (requestedEffectiveWhen is not null && requestedEffectiveWhen is not (EffectiveImmediate or EffectiveNextInvoice))
                return Fail("invalid_effective_when", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, requestedEffectiveWhen);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} CompanyId={CompanyId} EntitlementId={EntitlementId} SeatsTotal={SeatsTotal} DurationMs={DurationMs}",
                "outcome",
                "no_action_needed",
                "target_quantity_already_active",
                companyId,
                entitlementId,
                entitlement.SeatsTotal,
                opWatch.ElapsedMilliseconds);

            _metrics?.CompanySeatQuantityChange("no_action_needed", "target_quantity_already_active", direction, requestedEffectiveWhen ?? "-");
            return Success(companyId, entitlementId, subscriptionId, entitlement, entitlement.SeatsTotal, request.TargetSeats, direction, requestedEffectiveWhen ?? EffectiveImmediate, null, nowUtc);
        }

        var effectiveWhen = requestedEffectiveWhen
                            ?? (direction == "increase" ? EffectiveImmediate : EffectiveNextInvoice);

        if (request.TargetSeats < entitlement.SeatsUsed)
            return Fail("target_below_seats_used", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);

        if (effectiveWhen is not (EffectiveImmediate or EffectiveNextInvoice))
            return Fail("invalid_effective_when", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, requestedEffectiveWhen);

        if (direction == "increase" && effectiveWhen != EffectiveImmediate)
            return Fail("invalid_effective_when_for_increase", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);

        if (direction == "decrease" && effectiveWhen != EffectiveNextInvoice)
            return Fail("invalid_effective_when_for_decrease", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);

        var readWatch = Stopwatch.StartNew();
        var current = await _stripeSubscriptions.GetSubscriptionAsync(subscriptionId, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId}",
            "dependency",
            "stripe",
            "get_subscription",
            "Stripe API",
            readWatch.ElapsedMilliseconds,
            true,
            current is not null,
            subscriptionId);

        if (current is null)
            return Fail("subscription_not_found", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);

        if (!string.IsNullOrWhiteSpace(company.StripeCustomerId)
            && !string.Equals(company.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);
        }

        var prorationBehavior = direction == "increase" ? "always_invoice" : "none";
        StripeSubscriptionSnapshot? updated;
        try
        {
            var updateWatch = Stopwatch.StartNew();
            updated = await _stripeSubscriptions.UpdateSubscriptionQuantityAsync(
                subscriptionId,
                request.TargetSeats,
                prorationBehavior,
                BuildIdempotencyKey(subscriptionId, request.TargetSeats, effectiveWhen),
                ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found} SubscriptionId={SubscriptionId} TargetSeats={TargetSeats} Direction={Direction} ProrationBehavior={ProrationBehavior}",
                "dependency",
                "stripe",
                "update_subscription_quantity",
                "Stripe API",
                updateWatch.ElapsedMilliseconds,
                true,
                updated is not null,
                subscriptionId,
                request.TargetSeats,
                direction,
                prorationBehavior);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} SubscriptionId={SubscriptionId} TargetSeats={TargetSeats}",
                "exception",
                "dependency_failed",
                "stripe",
                "update_subscription_quantity",
                subscriptionId,
                request.TargetSeats);

            if (direction == "increase")
            {
                await NotifySeatIncreaseFailureSafeAsync(
                    actor,
                    company,
                    current,
                    entitlement,
                    subscriptionId,
                    request.TargetSeats,
                    effectiveWhen,
                    prorationBehavior,
                    "stripe_update_failed",
                    ct);
            }

            return Fail("stripe_update_failed", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);
        }

        if (updated is null)
        {
            if (direction == "increase")
            {
                await NotifySeatIncreaseFailureSafeAsync(
                    actor,
                    company,
                    current,
                    entitlement,
                    subscriptionId,
                    request.TargetSeats,
                    effectiveWhen,
                    prorationBehavior,
                    "stripe_update_returned_null",
                    ct);
            }

            return Fail("stripe_update_failed", opWatch, nowUtc, companyId, entitlementId, subscriptionId, entitlement.SeatsTotal, request.TargetSeats, entitlement.SeatsUsed, direction, effectiveWhen);
        }

        var projected = new Entitlement
        {
            CompanyId = entitlement.CompanyId,
            EntitlementId = entitlement.EntitlementId,
            SeatsTotal = request.TargetSeats,
            SeatsUsed = entitlement.SeatsUsed,
            IsOverCapacity = entitlement.SeatsUsed > request.TargetSeats,
            StartUtc = entitlement.StartUtc,
            EndUtc = entitlement.EndUtc,
            Status = entitlement.Status,
            UpdatedAtUtc = nowUtc
        };

        var writeWatch = Stopwatch.StartNew();
        await _entitlements.UpsertAsync(projected, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success} CompanyId={CompanyId} EntitlementId={EntitlementId}",
            "persistence",
            "entitlement.upsert_seat_quantity",
            "Entitlements",
            writeWatch.ElapsedMilliseconds,
            true,
            companyId,
            entitlementId);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} CompanyId={CompanyId} EntitlementId={EntitlementId} SubscriptionId={SubscriptionId} Direction={Direction} PreviousSeats={PreviousSeats} TargetSeats={TargetSeats} SeatsUsed={SeatsUsed} EffectiveWhen={EffectiveWhen} DurationMs={DurationMs}",
            "outcome",
            "completed",
            direction == "increase" ? "seat_quantity_increase_applied" : "seat_quantity_decrease_applied_safe",
            companyId,
            entitlementId,
            updated.SubscriptionId,
            direction,
            entitlement.SeatsTotal,
            request.TargetSeats,
            entitlement.SeatsUsed,
            effectiveWhen,
            opWatch.ElapsedMilliseconds);

        _metrics?.CompanySeatQuantityChange(
            "completed",
            direction == "increase" ? "seat_quantity_increase_applied" : "seat_quantity_decrease_applied_safe",
            direction,
            effectiveWhen);

        return Success(companyId, entitlementId, updated.SubscriptionId, projected, entitlement.SeatsTotal, request.TargetSeats, direction, effectiveWhen, updated.CurrentPeriodEndUtc, nowUtc);
    }

    private async Task NotifySeatIncreaseFailureSafeAsync(
        User actor,
        Company company,
        StripeSubscriptionSnapshot current,
        Entitlement entitlement,
        string subscriptionId,
        int targetSeats,
        string effectiveWhen,
        string prorationBehavior,
        string reason,
        CancellationToken ct)
    {
        if (_adminPaymentAlerts is null)
            return;

        AdminPaymentAlertDeliveryResult delivery;
        try
        {
            delivery = await _adminPaymentAlerts.NotifyAsync(new AdminPaymentAlert
            {
                OperationName = OperationName,
                FailureStage = "company_seat_quantity_increase",
                FailureReason = reason,
                Severity = "Critical",
                OccurredAtUtc = _clock.UtcNow,
                UserPk = Buckets.UserBucketPk(actor.UserId),
                UserId = actor.UserId,
                Email = FirstNonBlank(actor.EmailNormalized, company.AdminEmailNormalized),
                PhoneE164 = actor.PhoneE164,
                ManyChatSubscriberId = actor.ManyChatSubscriberId,
                CompanyId = company.CompanyId,
                StripeCustomerId = FirstNonBlank(current.CustomerId, company.StripeCustomerId),
                StripeSubscriptionId = subscriptionId,
                PlanType = "fleet",
                PriceId = current.PriceId,
                Details =
                {
                    ["companyName"] = company.Name,
                    ["companyAdminEmail"] = company.AdminEmailNormalized,
                    ["previousSeats"] = entitlement.SeatsTotal.ToString(),
                    ["targetSeats"] = targetSeats.ToString(),
                    ["seatsUsed"] = entitlement.SeatsUsed.ToString(),
                    ["effectiveWhen"] = effectiveWhen,
                    ["prorationBehavior"] = prorationBehavior,
                    ["subscriptionStatus"] = current.Status,
                    ["stripeQuantity"] = current.Quantity?.ToString(),
                    ["stripeInterval"] = current.Interval,
                    ["currentPeriodEndUtc"] = current.CurrentPeriodEndUtc?.UtcDateTime.ToString("O")
                }
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Admin payment alert failed after company seat increase failure. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} OperationName={OperationName} CompanyId={CompanyId} SubscriptionId={SubscriptionId} TargetSeats={TargetSeats}",
                "exception",
                "dependency_failed",
                "admin_payment_alert_unexpected_exception",
                OperationName,
                company.CompanyId,
                subscriptionId,
                targetSeats);
            return;
        }

        _logger.LogInformation(
            "Admin payment alert outcome. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} StatusCode={StatusCode} OperationName={OperationName} CompanyId={CompanyId} SubscriptionId={SubscriptionId} TargetSeats={TargetSeats}",
            "outcome",
            ToAdminPaymentAlertOutcome(delivery.Status),
            delivery.Reason,
            delivery.StatusCode,
            OperationName,
            company.CompanyId,
            subscriptionId,
            targetSeats);
    }

    private UpdateCompanySeatQuantityResult Fail(
        string error,
        Stopwatch opWatch,
        DateTimeOffset requestedAtUtc,
        string? companyId,
        string? entitlementId,
        string? subscriptionId,
        int previousSeats,
        int targetSeats,
        int seatsUsed,
        string direction,
        string? effectiveWhen)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} CompanyId={CompanyId} EntitlementId={EntitlementId} SubscriptionId={SubscriptionId} Direction={Direction} PreviousSeats={PreviousSeats} TargetSeats={TargetSeats} SeatsUsed={SeatsUsed} EffectiveWhen={EffectiveWhen} DurationMs={DurationMs}",
            "outcome",
            error == "forbidden" ? "denied" : "validation_failed",
            error,
            companyId,
            entitlementId,
            subscriptionId,
            direction,
            previousSeats,
            targetSeats,
            seatsUsed,
            effectiveWhen,
            opWatch.ElapsedMilliseconds);

        _metrics?.CompanySeatQuantityChange("failed", error, direction, effectiveWhen ?? "-");

        return new UpdateCompanySeatQuantityResult
        {
            Result = false,
            Error = error,
            CompanyId = companyId,
            EntitlementId = entitlementId,
            SubscriptionId = subscriptionId,
            PreviousSeatsTotal = previousSeats,
            TargetSeats = targetSeats,
            SeatsUsed = seatsUsed,
            Direction = direction,
            EffectiveWhen = effectiveWhen,
            RequestedAtUtc = requestedAtUtc
        };
    }

    private static UpdateCompanySeatQuantityResult Success(
        string companyId,
        string entitlementId,
        string subscriptionId,
        Entitlement entitlement,
        int previousSeats,
        int targetSeats,
        string direction,
        string effectiveWhen,
        DateTimeOffset? currentPeriodEndUtc,
        DateTimeOffset requestedAtUtc)
        => new()
        {
            Result = true,
            Error = null,
            CompanyId = companyId,
            EntitlementId = entitlementId,
            SubscriptionId = subscriptionId,
            PreviousSeatsTotal = previousSeats,
            TargetSeats = targetSeats,
            SeatsUsed = entitlement.SeatsUsed,
            IsOverCapacity = entitlement.IsOverCapacity,
            Direction = direction,
            EffectiveWhen = effectiveWhen,
            CurrentPeriodEndUtc = currentPeriodEndUtc,
            RequestedAtUtc = requestedAtUtc
        };

    private static bool IsAuthorizedCompanyActor(User actor, Company company)
    {
        var actorInCompany = !string.IsNullOrWhiteSpace(actor.CompanyId)
                             && string.Equals(actor.CompanyId, company.CompanyId, StringComparison.OrdinalIgnoreCase);

        var actorIsAdmin = !string.IsNullOrWhiteSpace(actor.EmailNormalized)
                           && !string.IsNullOrWhiteSpace(company.AdminEmailNormalized)
                           && string.Equals(actor.EmailNormalized, company.AdminEmailNormalized, StringComparison.OrdinalIgnoreCase);

        return actorInCompany || actorIsAdmin;
    }

    private static string ResolveEntitlementIdForFleetSubscription(string subscriptionId)
        => $"ent_{subscriptionId.Trim()}";

    private static string? TryGetSubscriptionIdFromEntitlementId(string? entitlementId)
    {
        var e = NullIfBlank(entitlementId);
        if (e is null || !e.StartsWith("ent_sub_", StringComparison.OrdinalIgnoreCase))
            return null;

        return e["ent_".Length..];
    }

    private static string? NormalizeEffectiveWhen(string? value)
    {
        var p = NullIfBlank(value)?.ToLowerInvariant();
        return p switch
        {
            null => null,
            "now" or "immediate" => EffectiveImmediate,
            "renewal" or "period_end" or "period-end" or "next_invoice" or "next-invoice" => EffectiveNextInvoice,
            _ => p
        };
    }

    private static string BuildIdempotencyKey(string subscriptionId, int targetSeats, string effectiveWhen)
        => $"company-seat-quantity:{subscriptionId.Trim()}:{targetSeats}:{effectiveWhen}";

    private static string ToAdminPaymentAlertOutcome(AdminPaymentAlertDeliveryStatus status)
        => status switch
        {
            AdminPaymentAlertDeliveryStatus.Sent => "company_seat_increase_alert_sent",
            AdminPaymentAlertDeliveryStatus.Skipped => "company_seat_increase_alert_skipped",
            _ => "company_seat_increase_alert_failed"
        };

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
