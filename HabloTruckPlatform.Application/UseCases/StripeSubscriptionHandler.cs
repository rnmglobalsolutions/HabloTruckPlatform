using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
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
        => HandleSubscriptionUpdatedAsync(ToSubUpdatedDto(data), ct);

    public Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeEventData data, CancellationToken ct = default)
        => HandleSubscriptionDeletedAsync(ToSubDeletedDto(data), ct);

    public Task<AccessDecision?> HandleInvoicePaidAsync(StripeEventData data, CancellationToken ct = default)
        => HandleInvoicePaidAsync(ToInvoicePaidDto(data), ct);

    public Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeEventData data, CancellationToken ct = default)
        => HandleInvoicePaymentFailedAsync(ToInvoiceFailedDto(data), ct);

    // =========================================================
    // CHECKOUT
    // =========================================================

    public async Task HandleCheckoutSessionCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.CustomerId))
            throw new ArgumentException("StripeEventData.CustomerId is required for checkout completion.");

        var nowUtc = _clock.UtcNow;

        var planTypeMeta = GetMeta(data, "planType")?.Trim().ToLowerInvariant();
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

        var priceId = NullIfBlank(data.PriceId);
        var interval = NullIfBlank(data.Interval);

        if (priceId is not null)
        {
            user.StripePriceId = priceId;
            user.IndividualPlanTerm = DeriveTermFromPriceId(priceId) ?? DeriveTermFromInterval(interval) ?? user.IndividualPlanTerm;
            user.PlanType = DerivePlanTypeFromPriceId(priceId) ?? user.PlanType;
        }
        else
        {
            user.IndividualPlanTerm = DeriveTermFromInterval(interval) ?? user.IndividualPlanTerm;
        }

        if (string.IsNullOrWhiteSpace(user.PlanType))
            user.PlanType = DerivePlanTypeFromMeta(planTypeMeta) ?? "individual_monthly";

        user.UpdatedAtUtc = nowUtc;

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        switch (NormalizeCheckoutPlan(user.PlanType))
        {
            case "individual":
                {
                    // Bootstrap only. subscription.updated is the source of truth.
                    user.SubscriptionStatus ??= "active";
                    user.UpdatedAtUtc = nowUtc;

                    await _userStore.UpsertAsync(user, ct);
                    await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

                    _logger.LogInformation(
                        "Checkout handled: individual userId={UserId} priceId={PriceId} term={Term}",
                        user.UserId,
                        user.StripePriceId,
                        user.IndividualPlanTerm);

                    break;
                }

            case "fleet":
                {
                    if (string.IsNullOrWhiteSpace(companyId))
                        throw new InvalidOperationException("fleet checkout requires metadata companyId.");

                    var seats = data.Quantity > 0 ? data.Quantity : ParseInt(seatsMeta, 0);
                    if (seats <= 0)
                        throw new InvalidOperationException("fleet checkout requires seats quantity > 0.");

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

                    var entitlement = new Entitlement
                    {
                        EntitlementId = ent.EntitlementId,
                        CompanyId = ent.CompanyId,
                        SeatsUsed = ent.SeatsUsed,
                        SeatsTotal = ent.SeatsTotal,
                        StartUtc = (ent.StartUtc == default) ? nowUtc : nowUtc,
                        Status = "active",
                        UpdatedAtUtc = nowUtc,
                    };

                    await _entitlementStore.UpsertAsync(entitlement, ct);

                    _logger.LogInformation(
                        "Checkout handled: fleet companyId={CompanyId} entId={EntitlementId} seats={Seats}",
                        companyId,
                        entitlementId,
                        seats);

                    break;
                }

            case "cdl_cohort":
                {
                    if (string.IsNullOrWhiteSpace(companyId))
                        throw new InvalidOperationException("cdl_cohort checkout requires metadata companyId.");

                    var seats = ParseInt(seatsMeta, data.Quantity);
                    if (seats <= 0)
                        throw new InvalidOperationException("cdl_cohort checkout requires seats > 0.");

                    var durationDays = ParseInt(durationDaysMeta, 90);
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

                    _logger.LogInformation(
                        "Checkout handled: cdl_cohort companyId={CompanyId} entId={EntitlementId} seats={Seats} endUtc={EndUtc}",
                        companyId,
                        entitlementId,
                        seats,
                        ent.EndUtc);

                    break;
                }

            default:
                throw new InvalidOperationException($"Unknown normalized checkout plan '{user.PlanType}'.");
        }
    }

    // =========================================================
    // SUBSCRIPTION / INVOICE DTO METHODS
    // =========================================================

    public async Task<AccessDecision?> HandleSubscriptionUpdatedAsync(
        StripeSubscriptionUpdate input,
        CancellationToken ct = default)
        => await ApplyReducerAsync(ToSignal(input), ct, triggerPaymentFailedFlowIfNeeded: false);

    public async Task<AccessDecision?> HandleSubscriptionDeletedAsync(
        StripeSubscriptionDeleted input,
        CancellationToken ct = default)
        => await ApplyReducerAsync(ToSignal(input), ct, triggerPaymentFailedFlowIfNeeded: false);

    public async Task<AccessDecision?> HandleInvoicePaidAsync(
        StripeInvoicePaid input,
        CancellationToken ct = default)
        => await ApplyReducerAsync(ToSignal(input), ct, triggerPaymentFailedFlowIfNeeded: false);

    public async Task<AccessDecision?> HandleInvoicePaymentFailedAsync(
        StripeInvoicePaymentFailed input,
        CancellationToken ct = default)
        => await ApplyReducerAsync(ToSignal(input), ct, triggerPaymentFailedFlowIfNeeded: true);

    // =========================================================
    // REDUCER PIPELINE
    // =========================================================

    private async Task<AccessDecision?> ApplyReducerAsync(
        StripeSignal signal,
        CancellationToken ct,
        bool triggerPaymentFailedFlowIfNeeded)
    {
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(signal.StripeCustomerId, ct);
        if (userRef is null) return null;

        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);
        if (user is null) return null;

        if (IsOutOfOrder(user, signal.StripeEventCreatedUtc))
        {
            return user.EffectiveAccess is not null
                ? new AccessDecision(
                    user.EffectiveAccess.Mode,
                    user.EffectiveAccess.Source,
                    user.EffectiveAccess.GraceEndsAtUtc,
                    "Ignored out-of-order Stripe event")
                : null;
        }

        var nowUtc = _clock.UtcNow;

        // 1) Apply signal facts to user shadow cache
        ApplySignalFacts(user, signal, nowUtc);

        // 2) Build facts for reducer
        var facts = new StripeSubscriptionFacts(
            CustomerId: user.StripeCustomerId,
            SubscriptionId: user.StripeSubscriptionId,
            Status: user.SubscriptionStatus,
            PriceId: user.StripePriceId,
            PlanTerm: user.IndividualPlanTerm,
            CancelAtPeriodEnd: user.StripeCancelAtPeriodEnd,
            CurrentPeriodEndUtc: user.StripeCurrentPeriodEndUtc,
            CanceledAtUtc: signal.CanceledAtUtc,
            EndedAtUtc: signal.EndedAtUtc);

        // 3) Reduce
        var reduced = SubscriptionReducer.Reduce(
            facts,
            nowUtc,
            _individualGracePolicy,
            user.IndividualGraceEndsAtUtc);

        // 4) Apply reducer result to user
        ApplyReducerResult(user, reduced);

        // 5) Maintain grace index
        await SyncGraceIndex(user, userRef.Value, ct);

        // 6) Recompute access + ManyChat sync (inside orchestrator)
        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        // 7) Persist
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        _logger.LogInformation(
            "Stripe reducer applied: kind={Kind} userId={UserId} reason={Reason} status={Status} periodEnd={PeriodEnd} graceEnd={GraceEnd}",
            signal.Kind,
            user.UserId,
            reduced.Reason,
            user.SubscriptionStatus,
            user.StripeCurrentPeriodEndUtc,
            user.IndividualGraceEndsAtUtc);

        if (triggerPaymentFailedFlowIfNeeded
            && !string.IsNullOrWhiteSpace(user.ManyChatSubscriberId)
            && (decision?.Mode == AccessMode.Grace || decision?.Mode == AccessMode.Blocked))
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

    private void ApplySignalFacts(User user, StripeSignal signal, DateTimeOffset nowUtc)
    {
        user.LastStripeEventId = signal.StripeEventId;
        user.LastStripeEventCreatedUtc = signal.StripeEventCreatedUtc;
        user.UpdatedAtUtc = nowUtc;

        if (!string.IsNullOrWhiteSpace(signal.StripeCustomerId))
            user.StripeCustomerId = signal.StripeCustomerId.Trim();

        if (!string.IsNullOrWhiteSpace(signal.StripeSubscriptionId))
            user.StripeSubscriptionId = signal.StripeSubscriptionId!.Trim();

        if (!string.IsNullOrWhiteSpace(signal.Status))
            user.SubscriptionStatus = NormalizeStatus(signal.Status);

        if (!string.IsNullOrWhiteSpace(signal.PriceId))
            user.StripePriceId = signal.PriceId!.Trim();

        // Prefer priceId as source of truth for term; interval is best-effort fallback
        var termFromPrice = DeriveTermFromPriceId(user.StripePriceId);
        var termFromInterval = DeriveTermFromInterval(signal.Interval);
        if (!string.IsNullOrWhiteSpace(termFromPrice))
            user.IndividualPlanTerm = termFromPrice;
        else if (!string.IsNullOrWhiteSpace(termFromInterval))
            user.IndividualPlanTerm = termFromInterval;

        var planFromPrice = DerivePlanTypeFromPriceId(user.StripePriceId);
        if (!string.IsNullOrWhiteSpace(planFromPrice))
            user.PlanType = planFromPrice;

        if (signal.CancelAtPeriodEnd is not null)
            user.StripeCancelAtPeriodEnd = signal.CancelAtPeriodEnd.Value;

        if (signal.CurrentPeriodEndUtc is not null)
            user.StripeCurrentPeriodEndUtc = signal.CurrentPeriodEndUtc;
    }

    private static void ApplyReducerResult(User user, IndividualEntitlementResult reduced)
    {
        switch (reduced.State)
        {
            case IndividualEntitlementState.Active:
            case IndividualEntitlementState.PaidThrough:
                user.IndividualGraceEndsAtUtc = null;
                break;

            case IndividualEntitlementState.Grace:
                user.IndividualGraceEndsAtUtc = reduced.GraceEndsAtUtc;
                break;

            case IndividualEntitlementState.Blocked:
            default:
                user.IndividualGraceEndsAtUtc = null;
                break;
        }
    }

    // =========================================================
    // GRACE INDEX
    // =========================================================

    private async Task SyncGraceIndex(User user, UserRef userRef, CancellationToken ct)
    {
        var nowUtc = _clock.UtcNow;

        if (user.IndividualGraceEndsAtUtc is not null && user.IndividualGraceEndsAtUtc > nowUtc)
        {
            var pk = $"{TablePrefixes.Grace}_{user.IndividualGraceEndsAtUtc.Value:yyyyMMddHH}";
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

    // =========================================================
    // HELPERS
    // =========================================================

    private static bool IsOutOfOrder(User user, DateTimeOffset eventCreatedUtc)
        => user.LastStripeEventCreatedUtc is not null && eventCreatedUtc < user.LastStripeEventCreatedUtc.Value;

    private static string? GetMeta(StripeEventData data, string key)
        => data.Metadata is not null && data.Metadata.TryGetValue(key, out var v) ? v : null;

    private static string? NormalizeEmail(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static int ParseInt(string? s, int fallback)
        => int.TryParse(s, out var n) ? n : fallback;

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? NormalizeStatus(string? status)
        => string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();

    private static string NormalizeCheckoutPlan(string? planType)
    {
        var p = (planType ?? "").Trim().ToLowerInvariant();

        return p switch
        {
            "individual" or "individual_monthly" or "individual_yearly" => "individual",
            "fleet" or "fleet_seat" or "company_seat" => "fleet",
            "cdl_cohort" => "cdl_cohort",
            _ => p
        };
    }

    private string? DeriveTermFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;
        priceId = priceId.Trim();

        if (priceId == _priceCatalog.IndividualMonthlyPriceId) return "monthly";
        if (priceId == _priceCatalog.IndividualYearlyPriceId) return "annual";

        return null;
    }

    private static string? DeriveTermFromInterval(string? interval)
    {
        if (string.IsNullOrWhiteSpace(interval)) return null;
        interval = interval.Trim().ToLowerInvariant();

        return interval switch
        {
            "month" => "monthly",
            "year" => "annual",
            _ => null
        };
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

    private static string? DerivePlanTypeFromMeta(string? planTypeMeta)
    {
        var p = (planTypeMeta ?? "").Trim().ToLowerInvariant();

        return p switch
        {
            "individual" => "individual_monthly",
            "individual_monthly" => "individual_monthly",
            "individual_yearly" => "individual_yearly",
            "annual" => "individual_yearly",
            "monthly" => "individual_monthly",
            "fleet" => "company_seat",
            "fleet_seat" => "company_seat",
            "company_seat" => "company_seat",
            "cdl_cohort" => "cdl_cohort",
            _ => null
        };
    }

    private static string ResolveEntitlementIdForFleet(string companyId, string? subscriptionId)
        => !string.IsNullOrWhiteSpace(subscriptionId)
            ? $"ent_{subscriptionId.Trim()}"
            : $"ent_{companyId}_default";

    // =========================================================
    // SIGNAL BUILDERS
    // =========================================================

    private static StripeSignal ToSignal(StripeSubscriptionUpdate i) => new(
        Kind: StripeSignalKind.SubscriptionUpdated,
        StripeEventId: i.StripeEventId,
        StripeEventCreatedUtc: i.StripeEventCreatedUtc,
        StripeCustomerId: i.StripeCustomerId,
        StripeSubscriptionId: i.StripeSubscriptionId,
        Status: i.SubscriptionStatus,
        PriceId: i.PriceId,
        Interval: i.Interval,
        CancelAtPeriodEnd: i.CancelAtPeriodEnd,
        CurrentPeriodEndUtc: i.CurrentPeriodEndUtc,
        CanceledAtUtc: i.CanceledAtUtc,
        EndedAtUtc: i.EndedAtUtc
    );

    private static StripeSignal ToSignal(StripeSubscriptionDeleted i) => new(
        Kind: StripeSignalKind.SubscriptionDeleted,
        StripeEventId: i.StripeEventId,
        StripeEventCreatedUtc: i.StripeEventCreatedUtc,
        StripeCustomerId: i.StripeCustomerId,
        StripeSubscriptionId: i.StripeSubscriptionId,
        Status: "deleted",
        PriceId: i.PriceId,
        Interval: i.Interval,
        CancelAtPeriodEnd: i.CancelAtPeriodEnd,
        CurrentPeriodEndUtc: i.CurrentPeriodEndUtc,
        CanceledAtUtc: i.CanceledAtUtc,
        EndedAtUtc: i.EndedAtUtc
    );

    private static StripeSignal ToSignal(StripeInvoicePaid i) => new(
        Kind: StripeSignalKind.InvoicePaid,
        StripeEventId: i.StripeEventId,
        StripeEventCreatedUtc: i.StripeEventCreatedUtc,
        StripeCustomerId: i.StripeCustomerId,
        StripeSubscriptionId: i.StripeSubscriptionId,
        Status: "active",
        PriceId: i.PriceId,
        Interval: i.Interval,
        CancelAtPeriodEnd: null,
        CurrentPeriodEndUtc: null,
        CanceledAtUtc: null,
        EndedAtUtc: null
    );

    private static StripeSignal ToSignal(StripeInvoicePaymentFailed i) => new(
        Kind: StripeSignalKind.InvoicePaymentFailed,
        StripeEventId: i.StripeEventId,
        StripeEventCreatedUtc: i.StripeEventCreatedUtc,
        StripeCustomerId: i.StripeCustomerId,
        StripeSubscriptionId: i.StripeSubscriptionId,
        Status: "past_due",
        PriceId: i.PriceId,
        Interval: i.Interval,
        CancelAtPeriodEnd: null,
        CurrentPeriodEndUtc: null,
        CanceledAtUtc: null,
        EndedAtUtc: null
    );

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
        Interval: d.Interval
    );

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
        Interval: d.Interval
    );

    private static StripeInvoicePaid ToInvoicePaidDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "",
        PriceId: d.PriceId,
        Interval: d.Interval
    );

    private static StripeInvoicePaymentFailed ToInvoiceFailedDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "",
        PriceId: d.PriceId,
        Interval: d.Interval
    );
}