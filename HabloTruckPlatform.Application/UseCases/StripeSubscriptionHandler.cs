using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.Stripex;
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
    private readonly GracePolicy _gracePolicy;
    private readonly IClock _clock;
    private readonly GracePolicy _individualGracePolicy;
    private readonly StripePriceCatalogOptions _priceCatalog;
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
        GracePolicy gracePolicy,
        IClock clock,
        GracePolicy individualGracePolicy,
        StripePriceCatalogOptions priceCatalog,
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
        _graceIndexStore = graceIndexStore;
        _gracePolicy = gracePolicy;
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

    public async Task HandleCheckoutCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        await HandleCheckoutSessionCompletedAsync(data, ct);
    }

    public Task<AccessDecision?> HandleSubscriptionUpdatedAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeSubscriptionUpdate(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId,
            SubscriptionStatus: data.Status ?? "unknown");

        return HandleSubscriptionUpdatedAsync(dto, ct);
    }

    public Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeSubscriptionDeleted(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId);

        return HandleSubscriptionDeletedAsync(dto, ct);
    }

    public Task<AccessDecision?> HandleInvoicePaidAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeInvoicePaid(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId);

        return HandleInvoicePaidAsync(dto, ct);
    }

    public Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeEventData data, CancellationToken ct = default)
    {
        var dto = new StripeInvoicePaymentFailed(
            StripeEventId: data.StripeEventId,
            StripeEventCreatedUtc: data.StripeEventCreatedUtc,
            StripeCustomerId: data.CustomerId ?? "",
            StripeSubscriptionId: data.SubscriptionId);

        return HandleInvoicePaymentFailedAsync(dto, ct);
    }

    // =========================================================
    // DTO-BASED METHODS (existing)
    // =========================================================

    public async Task HandleCheckoutSessionCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.CustomerId))
            throw new ArgumentException("StripeEventData.CustomerId is required for checkout completion.");

        var nowUtc = _clock.UtcNow;

        // ---- Read metadata
        var planType = GetMeta(data, "planType")?.Trim().ToLowerInvariant() ?? "individual";
        var cohortId = GetMeta(data, "ht_cohort")?.Trim();                   // recommended
        var schoolId = GetMeta(data, "ht_school_id")?.Trim();               // recommended


        // B2B
        var companyId = GetMeta(data, "companyId")?.Trim() ?? GetMeta(data, "ht_company_id")?.Trim();
        var companyName = GetMeta(data, "companyName")?.Trim();
        var seatsMeta = GetMeta(data, "seats");
        var durationDaysMeta = GetMeta(data, "durationDays");

        // ---- Ensure User exists (prefer email, then manychat)
        var emailNormalized = NormalizeEmail(data.CustomerEmail) ?? NormalizeEmail(GetMeta(data, "email"));
        var manyChatSubscriberId = GetMeta(data, "manychatSubscriberId") ?? GetMeta(data, "subscriberId");
        var phoneE164 = GetMeta(data, "phone");

        var user = await _userStore.GetOrCreateAsync(emailNormalized, manyChatSubscriberId, phoneE164, ct);

        // ---- Attach Stripe facts to user (very important for lookups)
        user.StripeCustomerId = data.CustomerId!.Trim();
        user.StripeSubscriptionId = string.IsNullOrWhiteSpace(data.SubscriptionId) ? user.StripeSubscriptionId : data.SubscriptionId!.Trim();
        user.UpdatedAtUtc = nowUtc;

        // attach cohort fields (optional)
        user.CohortId = string.IsNullOrWhiteSpace(cohortId) ? user.CohortId : cohortId;
        user.SchoolId = string.IsNullOrWhiteSpace(schoolId) ? user.SchoolId : schoolId;

        // --------- Determine plan by PriceId (source of truth)
        var priceId = data.PriceId?.Trim();

        if (string.IsNullOrWhiteSpace(priceId))
        {
            // fallback: if metadata says planType, accept it; else assume individual monthly
            planType ??= "individual";
        }
        else
        {
            if (priceId == _priceCatalog.IndividualMonthlyPriceId)
                planType = "individual_monthly";
            else if (priceId == _priceCatalog.IndividualYearlyPriceId)
                planType = "individual_yearly";
            else if (priceId == _priceCatalog.FleetSeatMonthlyPriceId)
                planType = "fleet_seat";
            else if (priceId == _priceCatalog.CdlCohort25PriceId
                  || priceId == _priceCatalog.CdlCohort50PriceId
                  || priceId == _priceCatalog.CdlCohort100PriceId)
                planType = "cdl_cohort";
            else
                planType ??= "individual"; // unknown price -> safe fallback
        }

        // store for analytics/debug
        user.PlanType = planType;

        // Persist + lookups (lookups are insert-only in PROD)
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        // ---- Handle plan types
        switch (planType)
        {
            case "individual":
            {
                // Set user to active (your domain method may differ)
                SubscriptionState.ApplyStripeStatus(
                    user,
                    newStatus: "active",
                    nowUtc: nowUtc,
                    gracePolicy: _gracePolicy);

                user.UpdatedAtUtc = nowUtc;
                await _userStore.UpsertAsync(user, ct);

                await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

                _logger.LogInformation("Checkout handled: individual userId={UserId}", user.UserId);
                break;
            }

            case "fleet":
            {
                if (string.IsNullOrWhiteSpace(companyId))
                    throw new InvalidOperationException("fleet checkout requires metadata companyId.");

                // Seats: prefer actual quantity (if parser supplies), else metadata
                var seats = data.Quantity > 0 ? data.Quantity : ParseInt(seatsMeta, fallback: 0);
                if (seats <= 0) throw new InvalidOperationException("fleet checkout requires seats quantity > 0.");

                // Ensure Company exists/updated
                await _companyStore.UpsertFromCheckoutAsync(
                    companyId: companyId!,
                    companyName: companyName,
                    adminEmailNormalized: emailNormalized,
                    stripeCustomerId: data.CustomerId,
                    ct: ct);

                // Create/Update entitlement (monthly subscription => EndUtc usually null; Stripe controls lifecycle)
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

                var updated = new Entitlement
                {
                    CompanyId = ent.CompanyId,
                    EntitlementId = ent.EntitlementId,

                    SeatsUsed = ent.SeatsUsed,
                    SeatsTotal = seats,

                    StartUtc = ent.StartUtc == default ? nowUtc : ent.StartUtc,
                    EndUtc = ent.EndUtc,

                    Status = "active",
                    UpdatedAtUtc = nowUtc
                };

                ent = updated;

                await _entitlementStore.UpsertAsync(ent, ct);

                // If you maintain expiry index only when EndUtc != null, no index needed here.

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

                // Cohort entitlement is time-bounded (prepaid)
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

                // ✅ index expiry for sweeper
                await _expiryIndex.UpsertAsync(new EntitlementRef(companyId!, entitlementId), ent.EndUtc.Value, ct);

                _logger.LogInformation("Checkout handled: cdl_cohort companyId={CompanyId} entId={EntitlementId} seats={Seats} endUtc={EndUtc}",
                    companyId, entitlementId, seats, ent.EndUtc);

                break;
            }

            default:
                throw new InvalidOperationException($"Unknown planType '{planType}'.");
        }
    }

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

        user.StripeCustomerId = input.StripeCustomerId;
        user.StripeSubscriptionId = input.StripeSubscriptionId;

        SubscriptionState.ApplyStripeStatus(
            user,
            newStatus: input.SubscriptionStatus,
            nowUtc: _clock.UtcNow,
            gracePolicy: _individualGracePolicy);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = _clock.UtcNow;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

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

        user.StripeCustomerId = input.StripeCustomerId;
        user.StripeSubscriptionId = input.StripeSubscriptionId;

        SubscriptionState.ApplyStripeStatus(
            user,
            newStatus: "deleted",
            nowUtc: _clock.UtcNow,
            gracePolicy: _individualGracePolicy);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = _clock.UtcNow;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

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

        SubscriptionState.ApplyStripeStatus(
            user,
            newStatus: "active",
            nowUtc: _clock.UtcNow,
            gracePolicy: _individualGracePolicy);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = _clock.UtcNow;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        return decision;
    }

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

        SubscriptionState.ApplyStripeStatus(
            user,
            newStatus: "past_due",
            nowUtc: _clock.UtcNow,
            gracePolicy: _individualGracePolicy);

        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = _clock.UtcNow;

        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        if (!string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
        {
            try
            {
                await _manyChatSync.TriggerPaymentFailedFlowAsync(user.ManyChatSubscriberId!, ct);
            }
            catch
            {
                // best-effort
            }
        }

        return decision;
    }

    // =========================================================
    // Helpers
    // =========================================================

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
            {
                await _graceIndexStore.DeleteAsync(pk!, rk!, ct);
            }
            else
            {
                await _graceIndexStore.DeleteForUserAsync(userRef, ct);
            }
        }
    }

    private static string? GetMeta(StripeEventData data, string key)
    {
        if (data.Metadata is null) return null;
        return data.Metadata.TryGetValue(key, out var v) ? v : null;
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        return email.Trim().ToLowerInvariant();
    }

    private static int ParseInt(string? s, int fallback)
        => int.TryParse(s, out var n) ? n : fallback;

    // Fleet entitlements should be stable per subscription (recommended)
    private static string ResolveEntitlementIdForFleet(string companyId, string? subscriptionId)
    {
        // If you have a better deterministic mapping, use it.
        // Best: "ent_{subscriptionId}" if present; else fallback to "ent_default"
        if (!string.IsNullOrWhiteSpace(subscriptionId))
            return $"ent_{subscriptionId.Trim()}";

        return $"ent_{companyId}_default";
    }
}