using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Handles Stripe subscription/invoice signals (already parsed into DTOs).
/// No Stripe SDK types here.
/// </summary>
public sealed class StripeSubscriptionHandler : IStripeSubscriptionHandler
{
    private readonly IUserResolver _userResolver;
    private readonly IUserStore _userStore;
    private readonly IGraceIndexStore _graceIndexStore;
    private readonly IManyChatSync _manyChatSync;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly IClock _clock;

    private readonly GracePolicy _individualGracePolicy;
    private readonly StripeOptions _priceCatalog;

    private readonly ICompanyStore _companyStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly IEntitlementExpiryIndexStore _expiryIndex;

    private readonly ILogger<StripeSubscriptionHandler> _logger;

    public StripeSubscriptionHandler(
        IUserResolver userResolver,
        IUserStore userStore,
        IGraceIndexStore graceIndexStore,
        IManyChatSync manyChatSync,
        AccessOrchestrator accessOrchestrator,
        IClock clock,
        GracePolicy individualGracePolicy,
        StripeOptions priceCatalog,
        ICompanyStore companyStore,
        IEntitlementStore entitlementStore,
        IEntitlementExpiryIndexStore expiryIndex,
        ILogger<StripeSubscriptionHandler> logger)
    {
        _userResolver = userResolver;
        _userStore = userStore;
        _graceIndexStore = graceIndexStore;
        _manyChatSync = manyChatSync;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;

        _individualGracePolicy = individualGracePolicy;
        _priceCatalog = priceCatalog;

        _companyStore = companyStore;
        _entitlementStore = entitlementStore;
        _expiryIndex = expiryIndex;

        _logger = logger;
    }

    // =========================================================
    // WEBHOOK-FRIENDLY METHODS (StripeEventData)
    // =========================================================

    public Task HandleCheckoutCompletedAsync(StripeEventData data, CancellationToken ct = default)
        => HandleCheckoutSessionCompletedAsync(data, ct);

    public Task<AccessDecision?> HandleSubscriptionUpdatedAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeSubscriptionUpdate(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId ?? "",
            SubscriptionStatus: data.Status ?? "unknown",
            CancelAtPeriodEnd: data.CancelAtPeriodEnd ?? false,
            CurrentPeriodEndUtc: data.CurrentPeriodEndUtc,
            CanceledAtUtc: data.CanceledAtUtc,
            EndedAtUtc: data.EndedAtUtc,
            PriceId: data.PriceId);

        return HandleSubscriptionUpdatedAsync(dto, ct);
    }

    public Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeSubscriptionDeleted(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId ?? "",
            CurrentPeriodEndUtc: data.CurrentPeriodEndUtc,
            CanceledAtUtc: data.CanceledAtUtc,
            EndedAtUtc: data.EndedAtUtc,
            PriceId: data.PriceId);

        return HandleSubscriptionDeletedAsync(dto, ct);
    }

    public Task<AccessDecision?> HandleInvoicePaidAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeInvoicePaid(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId ?? "");

        return HandleInvoicePaidAsync(dto, ct);
    }

    public Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeInvoicePaymentFailed(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId ?? "");

        return HandleInvoicePaymentFailedAsync(dto, ct);
    }

    // =========================================================
    // CHECKOUT (bootstrap user + B2B entitlements)
    // =========================================================

    public async Task HandleCheckoutSessionCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.CustomerId))
            throw new ArgumentException("StripeEventData.CustomerId is required for checkout completion.");

        var nowUtc = _clock.UtcNow;

        // ---- Metadata
        var planTypeMeta = GetMeta(data, "planType")?.Trim().ToLowerInvariant(); // optional
        var cohortId = GetMeta(data, "ht_cohort")?.Trim();
        var schoolId = GetMeta(data, "ht_school_id")?.Trim();

        var companyId = GetMeta(data, "companyId")?.Trim() ?? GetMeta(data, "ht_company_id")?.Trim();
        var companyName = GetMeta(data, "companyName")?.Trim();

        var seatsMeta = GetMeta(data, "seats");
        var durationDaysMeta = GetMeta(data, "durationDays");

        // ---- Identify user
        var emailNormalized = NormalizeEmail(data.CustomerEmail) ?? NormalizeEmail(GetMeta(data, "email"));
        var manyChatSubscriberId = GetMeta(data, "manychatSubscriberId") ?? GetMeta(data, "subscriberId");
        var phoneE164 = GetMeta(data, "phone");

        var user = await _userStore.GetOrCreateAsync(emailNormalized, manyChatSubscriberId, phoneE164, ct);

        // ---- Attach Stripe facts
        user.StripeCustomerId = data.CustomerId!.Trim();
        user.StripeSubscriptionId = string.IsNullOrWhiteSpace(data.SubscriptionId)
            ? user.StripeSubscriptionId
            : data.SubscriptionId!.Trim();

        user.UpdatedAtUtc = nowUtc;

        // optional cohort/school
        user.CohortId = string.IsNullOrWhiteSpace(cohortId) ? user.CohortId : cohortId;
        user.SchoolId = string.IsNullOrWhiteSpace(schoolId) ? user.SchoolId : schoolId;

        // ---- Determine plan by PriceId (source of truth)
        var priceId = data.PriceId?.Trim();
        var (planFamily, term) = ResolvePlanFamilyAndTerm(priceId, planTypeMeta);

        user.PlanType = planFamily;                 // "individual" | "fleet" | "cdl_cohort"
        user.IndividualPlanTerm = term;             // "monthly" | "annual" | null (for b2b)
        user.StripePriceId = priceId;

        // Persist + lookups early (so next webhooks can resolve)
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        // ---- Handle plan families
        switch (planFamily)
        {
            case "individual":
                {
                    // Bootstraps to active. Subsequent subscription.updated will set period_end, cancel_at_period_end, etc.
                    SubscriptionState.ApplyStripeStatus(
                        user,
                        newStatus: "active",
                        nowUtc: nowUtc,
                        gracePolicy: _individualGracePolicy);

                    user.UpdatedAtUtc = nowUtc;
                    await _userStore.UpsertAsync(user, ct);

                    await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

                    _logger.LogInformation("Checkout handled: individual userId={UserId} term={Term} priceId={PriceId}",
                        user.UserId, user.IndividualPlanTerm, user.StripePriceId);

                    break;
                }

            case "fleet":
                {
                    if (string.IsNullOrWhiteSpace(companyId))
                        throw new InvalidOperationException("fleet checkout requires metadata companyId.");

                    var seats = data.Quantity > 0 ? data.Quantity : ParseInt(seatsMeta, fallback: 0);
                    if (seats <= 0) throw new InvalidOperationException("fleet checkout requires seats quantity > 0.");

                    await _companyStore.UpsertFromCheckoutAsync(
                        companyId: companyId!,
                        companyName: companyName,
                        adminEmailNormalized: emailNormalized,
                        stripeCustomerId: data.CustomerId,
                        ct: ct);

                    var entitlementId = ResolveEntitlementIdForFleet(companyId!, data.SubscriptionId);

                    var ent = await _entitlementStore.GetAsync(companyId!, entitlementId, ct)
                              ?? new Entitlement
                              {
                                  CompanyId = companyId!,
                                  EntitlementId = entitlementId,
                                  SeatsUsed = 0,
                                  SeatsTotal = seats,
                                  StartUtc = nowUtc,
                                  Status = "active"
                              };

                    var newEntitlement = new Entitlement
                    {
                        CompanyId = ent.CompanyId,
                        EntitlementId = ent.EntitlementId,
                        SeatsUsed = ent.SeatsUsed, // keep existing used seats if any
                        SeatsTotal = seats,       // update total seats to new quantity
                        StartUtc = ent.StartUtc == default ? nowUtc : ent.StartUtc, // keep existing start if any
                        Status = "active",
                        UpdatedAtUtc = nowUtc
                    };

                    await _entitlementStore.UpsertAsync(newEntitlement, ct);

                    _logger.LogInformation("Checkout handled: fleet companyId={CompanyId} entId={EntitlementId} seats={Seats}",
                        companyId, entitlementId, seats);

                    break;
                }

            case "cdl_cohort":
                {
                    if (string.IsNullOrWhiteSpace(companyId))
                        throw new InvalidOperationException("cdl_cohort checkout requires metadata companyId.");

                    var seats = ParseInt(seatsMeta, fallback: data.Quantity);
                    if (seats <= 0) throw new InvalidOperationException("cdl_cohort checkout requires seats > 0.");

                    var durationDays = ParseInt(durationDaysMeta, fallback: 90);
                    if (durationDays <= 0) durationDays = 90;

                    await _companyStore.UpsertFromCheckoutAsync(
                        companyId: companyId!,
                        companyName: companyName,
                        adminEmailNormalized: emailNormalized,
                        stripeCustomerId: data.CustomerId,
                        ct: ct);

                    var entitlementId = UlidIds.NewEntitlementId();

                    var ent = new Entitlement
                    {
                        CompanyId = companyId!,
                        EntitlementId = entitlementId,
                        Status = "active",
                        SeatsTotal = seats,
                        SeatsUsed = 0,
                        StartUtc = nowUtc,
                        UpdatedAtUtc = nowUtc,
                        EndUtc = nowUtc.AddDays(durationDays)
                    };

                    await _entitlementStore.CreateAsync(ent, ct);

                    await _expiryIndex.UpsertAsync(new EntitlementRef(companyId!, entitlementId), ent.EndUtc!.Value, ct);

                    _logger.LogInformation("Checkout handled: cdl_cohort companyId={CompanyId} entId={EntitlementId} seats={Seats} endUtc={EndUtc}",
                        companyId, entitlementId, seats, ent.EndUtc);

                    break;
                }

            default:
                throw new InvalidOperationException($"Unknown plan family '{planFamily}'.");
        }
    }

    // =========================================================
    // SUBSCRIPTION UPDATED (upgrade/downgrade safe)
    // =========================================================

    public async Task<AccessDecision?> HandleSubscriptionUpdatedAsync(
        StripeSubscriptionUpdate input,
        CancellationToken ct = default)
    {
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(input.StripeCustomerId, ct);
        if (userRef is null) return null;

        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);
        if (user is null) return null;

        if (IsOutOfOrder(user, input.StripeEventCreatedUtc))
            return ExistingDecisionOrNull(user, "Ignored out-of-order event");

        var nowUtc = _clock.UtcNow;

        // ---- persist Stripe facts
        user.StripeCustomerId = input.StripeCustomerId?.Trim();
        user.StripeSubscriptionId = input.StripeSubscriptionId?.Trim();
        user.SubscriptionStatus = NormalizeStatus(input.SubscriptionStatus);

        user.StripeCancelAtPeriodEnd = input.CancelAtPeriodEnd;
        user.StripeCurrentPeriodEndUtc = input.CurrentPeriodEndUtc ?? user.StripeCurrentPeriodEndUtc;

        // priceId -> term
        if (!string.IsNullOrWhiteSpace(input.PriceId))
        {
            user.StripePriceId = input.PriceId!.Trim();
            user.IndividualPlanTerm = ResolveIndividualTermFromPrice(user.StripePriceId);
            user.PlanType ??= "individual";
        }

        // ---- anti false-grace rules
        if (IsPaidThrough(user, nowUtc) || IsActiveLike(user.SubscriptionStatus))
        {
            // paid-through or active: never grace
            user.IndividualGraceEndsAtUtc = null;
        }
        else if (IsDelinquentStatus(user.SubscriptionStatus))
        {
            // delinquent AND not paid-through => start/extend grace
            SubscriptionState.ApplyStripeStatus(user, "past_due", nowUtc, _individualGracePolicy);
        }
        else
        {
            // canceled/other and not paid-through => treat as terminal -> grace policy decides
            SubscriptionState.ApplyStripeStatus(user, user.SubscriptionStatus ?? "canceled", nowUtc, _individualGracePolicy);
        }

        // ---- audit
        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        // maintain grace index
        await SyncGraceIndex(user, userRef.Value, ct);

        // recompute + persist + ManyChat inside orchestrator
        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

        // keep lookups in sync
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

    // =========================================================
    // SUBSCRIPTION DELETED (paid-through aware)
    // =========================================================

    public async Task<AccessDecision?> HandleSubscriptionDeletedAsync(
        StripeSubscriptionDeleted input,
        CancellationToken ct = default)
    {
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(input.StripeCustomerId, ct);
        if (userRef is null) return null;

        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);
        if (user is null) return null;

        if (IsOutOfOrder(user, input.StripeEventCreatedUtc))
            return ExistingDecisionOrNull(user, "Ignored out-of-order event");

        var nowUtc = _clock.UtcNow;

        user.StripeCustomerId = input.StripeCustomerId?.Trim();
        user.StripeSubscriptionId = input.StripeSubscriptionId?.Trim();

        // keep current_period_end if Stripe provided it
        user.StripeCurrentPeriodEndUtc = input.CurrentPeriodEndUtc ?? user.StripeCurrentPeriodEndUtc;

        // set price/term if provided
        if (!string.IsNullOrWhiteSpace(input.PriceId))
        {
            user.StripePriceId = input.PriceId!.Trim();
            user.IndividualPlanTerm = ResolveIndividualTermFromPrice(user.StripePriceId);
        }

        // IMPORTANT:
        // If paid-through, do NOT start grace. Keep FULL until paid-through expires.
        if (IsPaidThrough(user, nowUtc))
        {
            user.SubscriptionStatus = "canceled";
            user.IndividualGraceEndsAtUtc = null;
        }
        else
        {
            // terminal without paid-through => grace policy applies
            SubscriptionState.ApplyStripeStatus(user, "deleted", nowUtc, _individualGracePolicy);
        }

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

    // =========================================================
    // INVOICE PAID (restore fast)
    // =========================================================

    public async Task<AccessDecision?> HandleInvoicePaidAsync(
        StripeInvoicePaid input,
        CancellationToken ct = default)
    {
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(input.StripeCustomerId, ct);
        if (userRef is null) return null;

        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);
        if (user is null) return null;

        if (IsOutOfOrder(user, input.StripeEventCreatedUtc))
            return ExistingDecisionOrNull(user, "Ignored out-of-order event");

        var nowUtc = _clock.UtcNow;

        // restore
        SubscriptionState.ApplyStripeStatus(user, "active", nowUtc, _individualGracePolicy);

        // clear grace instantly
        user.IndividualGraceEndsAtUtc = null;

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

    // =========================================================
    // INVOICE PAYMENT FAILED (paid-through aware)
    // =========================================================

    public async Task<AccessDecision?> HandleInvoicePaymentFailedAsync(
        StripeInvoicePaymentFailed input,
        CancellationToken ct = default)
    {
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(input.StripeCustomerId, ct);
        if (userRef is null) return null;

        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);
        if (user is null) return null;

        if (IsOutOfOrder(user, input.StripeEventCreatedUtc))
            return ExistingDecisionOrNull(user, "Ignored out-of-order event");

        var nowUtc = _clock.UtcNow;

        // ✅ critical: if still paid-through, ignore to avoid false grace during proration/plan change
        if (IsPaidThrough(user, nowUtc))
        {
            user.LastStripeEventId = input.StripeEventId;
            user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
            user.UpdatedAtUtc = nowUtc;

            await _userStore.UpsertAsync(user, ct);
            return await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);
        }

        // start/extend grace
        SubscriptionState.ApplyStripeStatus(user, "past_due", nowUtc, _individualGracePolicy);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        // recovery flow best-effort
        if (!string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
        {
            try { await _manyChatSync.TriggerPaymentFailedFlowAsync(user.ManyChatSubscriberId!, ct); }
            catch { /* best-effort */ }
        }

        return decision;
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static AccessDecision? ExistingDecisionOrNull(User user, string reason)
        => user.EffectiveAccess is not null
            ? new AccessDecision(user.EffectiveAccess.Mode, user.EffectiveAccess.Source, user.EffectiveAccess.GraceEndsAtUtc, reason)
            : null;

    private static string? NormalizeStatus(string? status)
        => string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();

    private static bool IsPaidThrough(User user, DateTimeOffset nowUtc)
        => user.StripeCurrentPeriodEndUtc is not null && user.StripeCurrentPeriodEndUtc > nowUtc;

    private static bool IsDelinquentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        status = status.Trim().ToLowerInvariant();
        return status is "past_due" or "unpaid" or "incomplete" or "incomplete_expired";
    }

    private static bool IsActiveLike(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        status = status.Trim().ToLowerInvariant();
        return status is "active" or "trialing";
    }

    private static bool IsOutOfOrder(User user, DateTimeOffset eventCreatedUtc)
    {
        if (user.LastStripeEventCreatedUtc is null) return false;
        return eventCreatedUtc < user.LastStripeEventCreatedUtc.Value;
    }

    private async Task SyncGraceIndex(User user, UserRef userRef, CancellationToken ct)
    {
        var nowUtc = _clock.UtcNow;

        if (user.IndividualGraceEndsAtUtc is not null && user.IndividualGraceEndsAtUtc > nowUtc)
        {
            var pk = $"{TablePrefixes.Grace}#{user.IndividualGraceEndsAtUtc.Value:yyyyMMddHH}";
            var rk = $"{user.IndividualGraceEndsAtUtc.Value.Ticks:D19}_{user.UserId}";

            user.CurrentGracePk = pk;
            user.CurrentGraceRk = rk;

            await _graceIndexStore.UpsertAsync(userRef, user.IndividualGraceEndsAtUtc.Value, ct);
        }
        else
        {
            var pk = user.CurrentGracePk;
            var rk = user.CurrentGraceRk;

            user.CurrentGracePk = null;
            user.CurrentGraceRk = null;

            if (!string.IsNullOrWhiteSpace(pk) && !string.IsNullOrWhiteSpace(rk))
                await _graceIndexStore.DeleteAsync(pk!, rk!, ct);
            else
                await _graceIndexStore.DeleteForUserAsync(userRef, ct);
        }
    }

    private static string? GetMeta(StripeEventData data, string key)
        => data.Metadata is not null && data.Metadata.TryGetValue(key, out var v) ? v : null;

    private static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static int ParseInt(string? s, int fallback)
        => int.TryParse(s, out var n) ? n : fallback;

    private static string ResolveEntitlementIdForFleet(string companyId, string? subscriptionId)
        => !string.IsNullOrWhiteSpace(subscriptionId)
            ? $"ent_{subscriptionId.Trim()}"
            : $"ent_{companyId}_default";

    /// <summary>
    /// Returns:
    /// planFamily: "individual" | "fleet" | "cdl_cohort"
    /// term: "monthly" | "annual" | null
    /// </summary>
    private (string planFamily, string? term) ResolvePlanFamilyAndTerm(string? priceId, string? planTypeMeta)
    {
        // If we have priceId, that's the truth.
        if (!string.IsNullOrWhiteSpace(priceId))
        {
            if (priceId == _priceCatalog.IndividualMonthlyPriceId)
                return ("individual", "monthly");

            if (priceId == _priceCatalog.IndividualYearlyPriceId)
                return ("individual", "annual");

            if (priceId == _priceCatalog.FleetSeatMonthlyPriceId)
                return ("fleet", null);

            if (priceId == _priceCatalog.CdlCohort25PriceId ||
                priceId == _priceCatalog.CdlCohort50PriceId ||
                priceId == _priceCatalog.CdlCohort100PriceId)
                return ("cdl_cohort", null);

            // Unknown price => safe fallback: treat as individual monthly
            return ("individual", "monthly");
        }

        // No priceId: use metadata
        var m = (planTypeMeta ?? "").Trim().ToLowerInvariant();

        return m switch
        {
            "fleet" or "fleet_seat" => ("fleet", null),
            "cdl_cohort" => ("cdl_cohort", null),
            "individual_yearly" or "annual" => ("individual", "annual"),
            "individual_monthly" or "monthly" => ("individual", "monthly"),
            _ => ("individual", "monthly")
        };
    }

    private string? ResolveIndividualTermFromPrice(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;

        if (priceId == _priceCatalog.IndividualYearlyPriceId) return "annual";
        if (priceId == _priceCatalog.IndividualMonthlyPriceId) return "monthly";

        // unknown -> don't overwrite existing term
        return null;
    }
}