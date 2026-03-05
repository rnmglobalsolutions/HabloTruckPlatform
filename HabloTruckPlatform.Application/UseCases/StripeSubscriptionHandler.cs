using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class StripeSubscriptionHandler : IStripeSubscriptionHandler
{
    private readonly IUserResolver _userResolver;
    private readonly IUserStore _userStore;
    private readonly IGraceIndexStore _graceIndexStore;
    private readonly IManyChatSync _manyChatSync;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly GracePolicy _individualGracePolicy;
    private readonly IClock _clock;
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
        => HandleSubscriptionUpdatedAsync(ToSubUpdatedDto(data), ct);

    public Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeEventData data, CancellationToken ct = default)
        => HandleSubscriptionDeletedAsync(ToSubDeletedDto(data), ct);

    public Task<AccessDecision?> HandleInvoicePaidAsync(StripeEventData data, CancellationToken ct = default)
        => HandleInvoicePaidAsync(ToInvoicePaidDto(data), ct);

    public Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeEventData data, CancellationToken ct = default)
        => HandleInvoicePaymentFailedAsync(ToInvoiceFailedDto(data), ct);

    // =========================================================
    // CHECKOUT: you already have this (kept as-is, but with small improvements)
    // =========================================================

    public async Task HandleCheckoutSessionCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.CustomerId))
            throw new ArgumentException("StripeEventData.CustomerId is required for checkout completion.");

        var nowUtc = _clock.UtcNow;

        // ---- Read metadata (your existing)
        var planType = GetMeta(data, "planType")?.Trim().ToLowerInvariant() ?? "individual";
        var cohortId = GetMeta(data, "ht_cohort")?.Trim();
        var schoolId = GetMeta(data, "ht_school_id")?.Trim();

        var companyId = GetMeta(data, "companyId")?.Trim() ?? GetMeta(data, "ht_company_id")?.Trim();
        var companyName = GetMeta(data, "companyName")?.Trim();
        var seatsMeta = GetMeta(data, "seats");
        var durationDaysMeta = GetMeta(data, "durationDays");

        var emailNormalized = NormalizeEmail(data.CustomerEmail) ?? NormalizeEmail(GetMeta(data, "email"));
        var manyChatSubscriberId = GetMeta(data, "manychatSubscriberId") ?? GetMeta(data, "subscriberId");
        var phoneE164 = GetMeta(data, "phone");

        var user = await _userStore.GetOrCreateAsync(emailNormalized, manyChatSubscriberId, phoneE164, ct);

        user.StripeCustomerId = data.CustomerId!.Trim();
        if (!string.IsNullOrWhiteSpace(data.SubscriptionId))
            user.StripeSubscriptionId = data.SubscriptionId!.Trim();

        user.CohortId = string.IsNullOrWhiteSpace(cohortId) ? user.CohortId : cohortId;
        user.SchoolId = string.IsNullOrWhiteSpace(schoolId) ? user.SchoolId : schoolId;

        // Determine term by priceId (source of truth)
        var priceId = data.PriceId?.Trim();
        if (!string.IsNullOrWhiteSpace(priceId))
        {
            user.StripePriceId = priceId;
            user.IndividualPlanTerm = DeriveTermFromPriceId(priceId);
            user.PlanType = DerivePlanTypeFromPriceId(priceId) ?? user.PlanType;
        }

        user.UpdatedAtUtc = nowUtc;

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        // Your existing planType switch (B2B cohorts etc.)
        // NOTE: Individual access will be stabilized by subscription.updated anyway.
        // Keep your existing logic here.
        switch (planType)
        {
            case "individual":
            case "individual_monthly":
            case "individual_yearly":
                {
                    // Do NOT start grace here.
                    // We can set a “hint”:
                    user.SubscriptionStatus ??= "active";
                    await _userStore.UpsertAsync(user, ct);
                    await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);
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
                    await _expiryIndex.UpsertAsync(new EntitlementRef(companyId!, entitlementId), ent.EndUtc.Value, ct);
                    break;
                }

            default:
                // ok to ignore unknown - subscription.updated will correct individual state
                _logger.LogInformation("Checkout planType not handled: {PlanType}", planType);
                break;
        }
    }

    // =========================================================
    // SUBSCRIPTION UPDATED (SOURCE OF TRUTH FOR UPGRADES/DOWNGRADES)
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
            return user.EffectiveAccess is not null
                ? new AccessDecision(user.EffectiveAccess.Mode, user.EffectiveAccess.Source, user.EffectiveAccess.GraceEndsAtUtc, "Ignored out-of-order event")
                : null;

        var nowUtc = _clock.UtcNow;

        _logger.LogInformation(
            "Stripe plan change detected user={UserId} priceId={PriceId} term={Term}",
            user.UserId,
            user.StripePriceId,
            user.IndividualPlanTerm);


        // 1) Update Stripe facts (this is where upgrades/downgrades are captured)
        user.StripeCustomerId = input.StripeCustomerId;
        user.StripeSubscriptionId = input.StripeSubscriptionId;

        user.SubscriptionStatus = NormalizeStatus(input.SubscriptionStatus);

        user.StripePriceId = string.IsNullOrWhiteSpace(input.PriceId) ? user.StripePriceId : input.PriceId!.Trim();
        user.IndividualPlanTerm = DeriveTermFromPriceId(user.StripePriceId);
        user.PlanType = DerivePlanTypeFromPriceId(user.StripePriceId) ?? user.PlanType;

        user.StripeCancelAtPeriodEnd = input.CancelAtPeriodEnd ?? false;
        user.StripeCurrentPeriodEndUtc = input.CurrentPeriodEndUtc ?? user.StripeCurrentPeriodEndUtc;

        // 2) Run reducer (State Machine) -> sets grace ONLY if needed
        ApplyIndividualReducer(user, nowUtc);

        // 3) Audit
        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        // 4) Maintain grace index (based on user.IndividualGraceEndsAtUtc)
        await SyncGraceIndex(user, userRef.Value, ct);

        // 5) Recompute access snapshot + ManyChat sync
        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

    // =========================================================
    // SUBSCRIPTION DELETED
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
            return user.EffectiveAccess is not null
                ? new AccessDecision(user.EffectiveAccess.Mode, user.EffectiveAccess.Source, user.EffectiveAccess.GraceEndsAtUtc, "Ignored out-of-order event")
                : null;

        var nowUtc = _clock.UtcNow;

        // Facts
        user.StripeCustomerId = input.StripeCustomerId;
        user.StripeSubscriptionId = input.StripeSubscriptionId;
        user.SubscriptionStatus = "deleted";

        if (input.CurrentPeriodEndUtc is not null)
            user.StripeCurrentPeriodEndUtc = input.CurrentPeriodEndUtc;

        if (input.CancelAtPeriodEnd is not null)
            user.StripeCancelAtPeriodEnd = input.CancelAtPeriodEnd.Value;

        // Reduce -> likely grace (unless paid-through still true)
        ApplyIndividualReducer(user, nowUtc);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

    // =========================================================
    // INVOICE PAID (HELPER SIGNAL)
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
            return user.EffectiveAccess is not null
                ? new AccessDecision(user.EffectiveAccess.Mode, user.EffectiveAccess.Source, user.EffectiveAccess.GraceEndsAtUtc, "Ignored out-of-order event")
                : null;

        var nowUtc = _clock.UtcNow;

        // We do NOT override subscription facts aggressively here.
        // But we can set a “hint”:
        user.SubscriptionStatus ??= "active";

        // Reduce using existing facts (paid-through wins if period_end is still future)
        ApplyIndividualReducer(user, nowUtc);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

    // =========================================================
    // INVOICE PAYMENT FAILED (HELPER SIGNAL + OPTIONAL RECOVERY FLOW)
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
            return user.EffectiveAccess is not null
                ? new AccessDecision(user.EffectiveAccess.Mode, user.EffectiveAccess.Source, user.EffectiveAccess.GraceEndsAtUtc, "Ignored out-of-order event")
                : null;

        var nowUtc = _clock.UtcNow;

        // Hint only: do not force grace if paid-through
        user.SubscriptionStatus ??= "past_due";

        ApplyIndividualReducer(user, nowUtc);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        // Only trigger recovery flow if reducer actually put them into Grace/Blocked (not PaidThrough)
        if (!string.IsNullOrWhiteSpace(user.ManyChatSubscriberId) &&
            decision.Mode is AccessMode.Grace or AccessMode.Blocked)
        {
            try { await _manyChatSync.TriggerPaymentFailedFlowAsync(user.ManyChatSubscriberId!, ct); }
            catch { /* best-effort */ }
        }

        return decision;
    }

    // =========================================================
    // Reducer application
    // =========================================================

    private void ApplyIndividualReducer(User user, DateTimeOffset nowUtc)
    {
        var facts = new StripeSubscriptionFacts(
            CustomerId: user.StripeCustomerId,
            SubscriptionId: user.StripeSubscriptionId,
            Status: user.SubscriptionStatus,
            PriceId: user.StripePriceId,
            PlanTerm: user.IndividualPlanTerm,
            CancelAtPeriodEnd: user.StripeCancelAtPeriodEnd,
            CurrentPeriodEndUtc: user.StripeCurrentPeriodEndUtc,
            CanceledAtUtc: null,
            EndedAtUtc: null);

        var result = IndividualSubscriptionStateMachine.Reduce(
            facts,
            nowUtc,
            _individualGracePolicy,
            user.IndividualGraceEndsAtUtc);

        // Apply derived results to user facts used by access rules
        switch (result.State)
        {
            case IndividualEntitlementState.Active:
            case IndividualEntitlementState.PaidThrough:
                user.IndividualGraceEndsAtUtc = null;
                // Keep subscription status as-is; Stripe is truth.
                break;

            case IndividualEntitlementState.Grace:
                user.IndividualGraceEndsAtUtc = result.GraceEndsAtUtc;
                break;

            case IndividualEntitlementState.Blocked:
            default:
                user.IndividualGraceEndsAtUtc = null;
                break;
        }
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static bool IsOutOfOrder(User user, DateTimeOffset eventCreatedUtc)
        => user.LastStripeEventCreatedUtc is not null && eventCreatedUtc < user.LastStripeEventCreatedUtc.Value;

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

    private static string NormalizeStatus(string? status)
        => string.IsNullOrWhiteSpace(status) ? "unknown" : status.Trim().ToLowerInvariant();

    private string? DeriveTermFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;
        priceId = priceId.Trim();

        if (priceId == _priceCatalog.IndividualMonthlyPriceId) return "monthly";
        if (priceId == _priceCatalog.IndividualYearlyPriceId) return "annual";

        // if unknown, keep null to avoid wrong term
        return null;
    }

    private string? DerivePlanTypeFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;
        priceId = priceId.Trim();

        if (priceId == _priceCatalog.IndividualMonthlyPriceId) return "individual_monthly";
        if (priceId == _priceCatalog.IndividualYearlyPriceId) return "individual_yearly";

        if (priceId == _priceCatalog.FleetSeatMonthlyPriceId) return "company_seat";

        if (priceId == _priceCatalog.CdlCohort25PriceId
            || priceId == _priceCatalog.CdlCohort50PriceId
            || priceId == _priceCatalog.CdlCohort100PriceId)
            return "cdl_cohort";

        return null;
    }

    private static string ResolveEntitlementIdForFleet(string companyId, string? subscriptionId)
        => !string.IsNullOrWhiteSpace(subscriptionId)
            ? $"ent_{subscriptionId.Trim()}"
            : $"ent_{companyId}_default";

    // DTO mappers (based on your StripeEventData extended fields)
    private static StripeSubscriptionUpdate ToSubUpdatedDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "",
        SubscriptionStatus: d.Status ?? "unknown",
        CancelAtPeriodEnd: d.CancelAtPeriodEnd,
        CurrentPeriodEndUtc: d.CurrentPeriodEndUtc,
        CanceledAtUtc: d.CanceledAtUtc,
        EndedAtUtc: d.EndedAtUtc,
        PriceId: d.PriceId,
        Interval: d.Interval);

    private static StripeSubscriptionDeleted ToSubDeletedDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "",
        CancelAtPeriodEnd: d.CancelAtPeriodEnd,
        CurrentPeriodEndUtc: d.CurrentPeriodEndUtc,
        CanceledAtUtc: d.CanceledAtUtc,
        EndedAtUtc: d.EndedAtUtc,
        PriceId: d.PriceId,
        Interval: d.Interval);

    private static StripeInvoicePaid ToInvoicePaidDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "");

    private static StripeInvoicePaymentFailed ToInvoiceFailedDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "");
}