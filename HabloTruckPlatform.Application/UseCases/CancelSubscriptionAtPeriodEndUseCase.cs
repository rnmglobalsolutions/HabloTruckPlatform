using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class CancelSubscriptionAtPeriodEndUseCase
{
    private const string ScopeIndividual = "individual";
    private const string ScopeCompany = "company";

    private readonly IUserStore _users;
    private readonly ICompanyStore _companies;
    private readonly IEntitlementStore _entitlements;
    private readonly IStripeSubscriptionGateway _stripeSubscriptions;
    private readonly IClock _clock;
    private readonly ILogger<CancelSubscriptionAtPeriodEndUseCase> _logger;
    private readonly IUserResolver? _userResolver;

    public CancelSubscriptionAtPeriodEndUseCase(
        IUserStore users,
        ICompanyStore companies,
        IEntitlementStore entitlements,
        IStripeSubscriptionGateway stripeSubscriptions,
        IClock clock,
        ILogger<CancelSubscriptionAtPeriodEndUseCase>? logger = null,
        IUserResolver? userResolver = null)
    {
        _users = users;
        _companies = companies;
        _entitlements = entitlements;
        _stripeSubscriptions = stripeSubscriptions;
        _clock = clock;
        _logger = logger ?? NullLogger<CancelSubscriptionAtPeriodEndUseCase>.Instance;
        _userResolver = userResolver;
    }

    public async Task<CancelSubscriptionAtPeriodEndResult> ExecuteAsync(
        CancelSubscriptionAtPeriodEndRequest request,
        CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            "entry",
            "cancel_subscription_at_period_end");

        if (request is null)
            return FailLogged("invalid_request", opWatch, null, null, null, null);

        var scope = NormalizeScope(request.Scope);
        if (scope is null)
            return FailLogged("invalid_scope", opWatch, request.Scope, null, null, null);

        var actorPk = NullIfBlank(request.ActorUserPk);
        var actorId = NullIfBlank(request.ActorUserId);

        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "cancel_subscription_at_period_end",
            ["CompanyId"] = request.CompanyId,
            ["UserId"] = actorId,
            ["ManyChatSubscriberId"] = request.ManyChatSubscriberId,
            ["SubscriptionId"] = request.SubscriptionId
        });

        var actorRef = await ResolveActorRefAsync(request, actorPk, actorId, ct);
        if (actorRef is null)
        {
            var reason = HasActorResolutionInput(request)
                ? "actor_not_found"
                : "actor_required";

            return FailLogged(reason, opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);
        }

        actorPk = actorRef.Value.UserPk;
        actorId = actorRef.Value.UserId;

        if (actorPk is null || actorId is null)
            return FailLogged("actor_required", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);

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
            return FailLogged("actor_not_found", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);

        var subscriptionId = NullIfBlank(request.SubscriptionId);
        Company? company = null;

        if (scope == ScopeIndividual)
        {
            subscriptionId ??= NullIfBlank(actor.StripeSubscriptionId);

            // If caller provided an explicit subscriptionId, require it to belong to actor context.
            if (!string.IsNullOrWhiteSpace(request.SubscriptionId)
                && !string.IsNullOrWhiteSpace(actor.StripeSubscriptionId)
                && !string.Equals(actor.StripeSubscriptionId, request.SubscriptionId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return FailLogged("forbidden", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);
            }
        }
        else
        {
            var companyId = NullIfBlank(request.CompanyId);
            if (companyId is null)
                return FailLogged("company_id_required", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);

            var companyWatch = Stopwatch.StartNew();
            company = await _companies.GetAsync(companyId, ct);

            _logger.LogDebug(
                "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
                "persistence",
                "company.get",
                "Companies",
                companyWatch.ElapsedMilliseconds,
                company is not null);

            if (company is null)
                return FailLogged("company_not_found", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);

            var actorInCompany = !string.IsNullOrWhiteSpace(actor.CompanyId)
                                 && string.Equals(actor.CompanyId, company.CompanyId, StringComparison.OrdinalIgnoreCase);

            var actorIsCompanyAdmin = !string.IsNullOrWhiteSpace(actor.EmailNormalized)
                                      && !string.IsNullOrWhiteSpace(company.AdminEmailNormalized)
                                      && string.Equals(actor.EmailNormalized, company.AdminEmailNormalized, StringComparison.OrdinalIgnoreCase);

            if (!actorInCompany && !actorIsCompanyAdmin)
                return FailLogged("forbidden", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);

            subscriptionId ??= TryGetSubscriptionIdFromEntitlementId(actor.SeatEntitlementId);
        }

        if (subscriptionId is null)
            return FailLogged("subscription_id_required", opWatch, scope, actorId, request.CompanyId, request.SubscriptionId);

        if (scope == ScopeCompany && company is not null)
        {
            var entitlementId = ResolveEntitlementIdForFleetSubscription(subscriptionId);
            var entitlementWatch = Stopwatch.StartNew();
            var entitlement = await _entitlements.GetAsync(company.CompanyId, entitlementId, ct);

            _logger.LogDebug(
                "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
                "persistence",
                "entitlement.get",
                "Entitlements",
                entitlementWatch.ElapsedMilliseconds,
                entitlement is not null);

            if (entitlement is null)
                return FailLogged("company_entitlement_not_found", opWatch, scope, actorId, company.CompanyId, subscriptionId);
        }

        var nowUtc = _clock.UtcNow;

        var stripeReadWatch = Stopwatch.StartNew();
        var current = await _stripeSubscriptions.GetSubscriptionAsync(subscriptionId, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
            "dependency",
            "stripe",
            "get_subscription",
            "Stripe API",
            stripeReadWatch.ElapsedMilliseconds,
            true,
            current is not null);

        if (current is null)
            return FailLogged("subscription_not_found", opWatch, scope, actorId, request.CompanyId, subscriptionId);

        // Authorization hardening: customer ownership must align with local context when available.
        if (scope == ScopeIndividual
            && !string.IsNullOrWhiteSpace(actor.StripeCustomerId)
            && !string.Equals(actor.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return FailLogged("forbidden", opWatch, scope, actorId, request.CompanyId, subscriptionId);
        }

        if (scope == ScopeCompany
            && company is not null
            && !string.IsNullOrWhiteSpace(company.StripeCustomerId)
            && !string.Equals(company.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return FailLogged("forbidden", opWatch, scope, actorId, company.CompanyId, subscriptionId);
        }

        if (current.CancelAtPeriodEnd)
            return SuccessLogged(scope, actorPk, actorId, current, nowUtc, alreadyScheduled: true, opWatch);

        StripeSubscriptionSnapshot? updated;
        try
        {
            var stripeWriteWatch = Stopwatch.StartNew();
            updated = await _stripeSubscriptions.ScheduleCancelAtPeriodEndAsync(
                subscriptionId,
                BuildIdempotencyKey(subscriptionId),
                ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
                "dependency",
                "stripe",
                "schedule_cancel_at_period_end",
                "Stripe API",
                stripeWriteWatch.ElapsedMilliseconds,
                true,
                updated is not null);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation}",
                "exception",
                "dependency_failed",
                "stripe",
                "schedule_cancel_at_period_end");

            return FailLogged("stripe_update_failed", opWatch, scope, actorId, request.CompanyId, subscriptionId);
        }

        if (updated is null)
            return FailLogged("stripe_update_failed", opWatch, scope, actorId, request.CompanyId, subscriptionId);

        // Optional local hint only (webhook/reducer remain source of truth).
        if (!string.IsNullOrWhiteSpace(actor.StripeSubscriptionId)
            && string.Equals(actor.StripeSubscriptionId, updated.SubscriptionId, StringComparison.OrdinalIgnoreCase))
        {
            actor.StripeCancelAtPeriodEnd = true;
            if (updated.CurrentPeriodEndUtc is not null)
                actor.StripeCurrentPeriodEndUtc = updated.CurrentPeriodEndUtc;

            actor.UpdatedAtUtc = nowUtc;

            var userWriteWatch = Stopwatch.StartNew();
            await _users.UpsertAsync(actor, ct);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "user.upsert",
                "Users",
                userWriteWatch.ElapsedMilliseconds,
                true);
        }

        return SuccessLogged(scope, actorPk, actorId, updated, nowUtc, alreadyScheduled: false, opWatch);
    }

    private CancelSubscriptionAtPeriodEndResult SuccessLogged(
        string scope,
        string actorUserPk,
        string actorUserId,
        StripeSubscriptionSnapshot snapshot,
        DateTimeOffset requestedAtUtc,
        bool alreadyScheduled,
        Stopwatch opWatch)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} Scope={Scope} SubscriptionId={SubscriptionId} AlreadyScheduled={AlreadyScheduled} DurationMs={DurationMs}",
            "outcome",
            alreadyScheduled ? "no_action_needed" : "completed",
            alreadyScheduled ? "already_scheduled" : "cancel_scheduled",
            scope,
            snapshot.SubscriptionId,
            alreadyScheduled,
            opWatch.ElapsedMilliseconds);

        return new CancelSubscriptionAtPeriodEndResult
        {
            Result = true,
            Scope = scope,
            ActorUserPk = actorUserPk,
            ActorUserId = actorUserId,
            SubscriptionId = snapshot.SubscriptionId,
            CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd,
            AlreadyScheduled = alreadyScheduled,
            EffectivePeriodEndUtc = snapshot.CurrentPeriodEndUtc,
            CurrentPeriodEndUtc = snapshot.CurrentPeriodEndUtc,
            CanceledAtUtc = snapshot.CanceledAtUtc,
            CancelRequestedAtUtc = requestedAtUtc,
            Error = null
        };
    }

    private CancelSubscriptionAtPeriodEndResult FailLogged(
        string error,
        Stopwatch opWatch,
        string? scope,
        string? actorUserId,
        string? companyId,
        string? subscriptionId)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} Scope={Scope} UserId={UserId} CompanyId={CompanyId} SubscriptionId={SubscriptionId} DurationMs={DurationMs}",
            "outcome",
            error == "forbidden" ? "denied" : "validation_failed",
            error,
            scope,
            actorUserId,
            companyId,
            subscriptionId,
            opWatch.ElapsedMilliseconds);

        return new CancelSubscriptionAtPeriodEndResult
        {
            Result = false,
            Scope = scope ?? ScopeIndividual,
            ActorUserPk = null,
            ActorUserId = actorUserId,
            SubscriptionId = subscriptionId,
            CancelAtPeriodEnd = false,
            AlreadyScheduled = false,
            EffectivePeriodEndUtc = null,
            CurrentPeriodEndUtc = null,
            CanceledAtUtc = null,
            CancelRequestedAtUtc = DateTimeOffset.UtcNow,
            Error = error
        };
    }

    private static string BuildIdempotencyKey(string subscriptionId)
        => $"hablotruck-cancel-{subscriptionId}";

    private async Task<UserRef?> ResolveActorRefAsync(
        CancelSubscriptionAtPeriodEndRequest request,
        string? actorPk,
        string? actorId,
        CancellationToken ct)
    {
        if (actorPk is not null && actorId is not null)
            return new UserRef(actorPk, actorId);

        var manyChatSubscriberId = NullIfBlank(request.ManyChatSubscriberId);
        if (manyChatSubscriberId is null || _userResolver is null)
            return null;

        return await _userResolver.ResolveByManyChatSubscriberIdAsync(manyChatSubscriberId, ct);
    }

    private static bool HasActorResolutionInput(CancelSubscriptionAtPeriodEndRequest request)
        => NullIfBlank(request.ActorUserPk) is not null
           || NullIfBlank(request.ActorUserId) is not null
           || NullIfBlank(request.ManyChatSubscriberId) is not null;

    private static string ResolveEntitlementIdForFleetSubscription(string subscriptionId)
        => $"ent_{subscriptionId.Trim()}";

    private static string? NormalizeScope(string? scope)
    {
        var s = (scope ?? ScopeIndividual).Trim().ToLowerInvariant();
        return s switch
        {
            ScopeIndividual => ScopeIndividual,
            ScopeCompany => ScopeCompany,
            "fleet" => ScopeCompany,
            _ => null
        };
    }

    private static string? TryGetSubscriptionIdFromEntitlementId(string? entitlementId)
    {
        var id = NullIfBlank(entitlementId);
        if (id is null) return null;

        if (!id.StartsWith("ent_", StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = id[4..].Trim();
        return string.IsNullOrWhiteSpace(suffix) ? null : suffix;
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
