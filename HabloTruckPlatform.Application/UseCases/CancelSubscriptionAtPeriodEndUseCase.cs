using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;

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

    public CancelSubscriptionAtPeriodEndUseCase(
        IUserStore users,
        ICompanyStore companies,
        IEntitlementStore entitlements,
        IStripeSubscriptionGateway stripeSubscriptions,
        IClock clock)
    {
        _users = users;
        _companies = companies;
        _entitlements = entitlements;
        _stripeSubscriptions = stripeSubscriptions;
        _clock = clock;
    }

    public async Task<CancelSubscriptionAtPeriodEndResult> ExecuteAsync(
        CancelSubscriptionAtPeriodEndRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail("invalid_request");

        var scope = NormalizeScope(request.Scope);
        if (scope is null)
            return Fail("invalid_scope");

        var actorPk = NullIfBlank(request.ActorUserPk);
        var actorId = NullIfBlank(request.ActorUserId);

        if (actorPk is null || actorId is null)
            return Fail("actor_required", scope);

        var actor = await _users.GetAsync(actorPk, actorId, ct);
        if (actor is null)
            return Fail("actor_not_found", scope);

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
                return Fail("forbidden", scope, request.SubscriptionId.Trim());
            }
        }
        else
        {
            var companyId = NullIfBlank(request.CompanyId);
            if (companyId is null)
                return Fail("company_id_required", scope);

            company = await _companies.GetAsync(companyId, ct);
            if (company is null)
                return Fail("company_not_found", scope);

            var actorInCompany = !string.IsNullOrWhiteSpace(actor.CompanyId)
                                 && string.Equals(actor.CompanyId, company.CompanyId, StringComparison.OrdinalIgnoreCase);

            var actorIsCompanyAdmin = !string.IsNullOrWhiteSpace(actor.EmailNormalized)
                                      && !string.IsNullOrWhiteSpace(company.AdminEmailNormalized)
                                      && string.Equals(actor.EmailNormalized, company.AdminEmailNormalized, StringComparison.OrdinalIgnoreCase);

            if (!actorInCompany && !actorIsCompanyAdmin)
                return Fail("forbidden", scope);

            subscriptionId ??= TryGetSubscriptionIdFromEntitlementId(actor.SeatEntitlementId);
        }

        if (subscriptionId is null)
            return Fail("subscription_id_required", scope);

        if (scope == ScopeCompany && company is not null)
        {
            var entitlementId = ResolveEntitlementIdForFleetSubscription(subscriptionId);
            var entitlement = await _entitlements.GetAsync(company.CompanyId, entitlementId, ct);
            if (entitlement is null)
                return Fail("company_entitlement_not_found", scope, subscriptionId);
        }

        var nowUtc = _clock.UtcNow;

        var current = await _stripeSubscriptions.GetSubscriptionAsync(subscriptionId, ct);
        if (current is null)
            return Fail("subscription_not_found", scope, subscriptionId);

        // Authorization hardening: customer ownership must align with local context when available.
        if (scope == ScopeIndividual
            && !string.IsNullOrWhiteSpace(actor.StripeCustomerId)
            && !string.Equals(actor.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", scope, subscriptionId);
        }

        if (scope == ScopeCompany
            && company is not null
            && !string.IsNullOrWhiteSpace(company.StripeCustomerId)
            && !string.Equals(company.StripeCustomerId, current.CustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("forbidden", scope, subscriptionId);
        }

        if (current.CancelAtPeriodEnd)
            return Success(scope, current, nowUtc, alreadyScheduled: true);

        StripeSubscriptionSnapshot? updated;
        try
        {
            updated = await _stripeSubscriptions.ScheduleCancelAtPeriodEndAsync(
                subscriptionId,
                BuildIdempotencyKey(subscriptionId),
                ct);
        }
        catch
        {
            return Fail("stripe_update_failed", scope, subscriptionId);
        }

        if (updated is null)
            return Fail("stripe_update_failed", scope, subscriptionId);

        // Optional local hint only (webhook/reducer remain source of truth).
        if (!string.IsNullOrWhiteSpace(actor.StripeSubscriptionId)
            && string.Equals(actor.StripeSubscriptionId, updated.SubscriptionId, StringComparison.OrdinalIgnoreCase))
        {
            actor.StripeCancelAtPeriodEnd = true;
            if (updated.CurrentPeriodEndUtc is not null)
                actor.StripeCurrentPeriodEndUtc = updated.CurrentPeriodEndUtc;

            actor.UpdatedAtUtc = nowUtc;
            await _users.UpsertAsync(actor, ct);
        }

        return Success(scope, updated, nowUtc, alreadyScheduled: false);
    }

    private static CancelSubscriptionAtPeriodEndResult Success(
        string scope,
        StripeSubscriptionSnapshot snapshot,
        DateTimeOffset requestedAtUtc,
        bool alreadyScheduled)
    {
        return new CancelSubscriptionAtPeriodEndResult
        {
            Result = true,
            Scope = scope,
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

    private static CancelSubscriptionAtPeriodEndResult Fail(
        string error,
        string scope = ScopeIndividual,
        string? subscriptionId = null)
    {
        return new CancelSubscriptionAtPeriodEndResult
        {
            Result = false,
            Scope = scope,
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

