using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

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
    private readonly IManyChatDispatchQueue? _manyChatDispatchQueue;
    private readonly IManyChatAudienceResolver? _manyChatAudienceResolver;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly IClock _clock;
    private readonly GracePolicy _individualGracePolicy;
    private readonly StripeOptions _priceCatalog;
    private readonly ICompanyStore _companyStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly CompanyAdminInviteService _companyAdminInviteService;
    private readonly IEntitlementExpiryIndexStore _expiryIndex;
    private readonly IFailedActionStore _failedActionStore;
    private readonly IStripeAdminClient _stripeAdminClient;
    private readonly BillingRecoveryManyChatNotifier _billingRecoveryNotifier;
    private readonly IAdminPaymentAlertNotifier? _adminPaymentAlerts;
    private readonly IAppMetrics? _metrics;
    private readonly ILogger<StripeSubscriptionHandler> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

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
        CompanyAdminInviteService companyAdminInviteService,
        IEntitlementExpiryIndexStore expiryIndex,
        IFailedActionStore failedActionStore,
        IStripeAdminClient stripeAdminClient,
        BillingRecoveryManyChatNotifier billingRecoveryNotifier,
        ILogger<StripeSubscriptionHandler> logger,
        IManyChatDispatchQueue? manyChatDispatchQueue = null,
        IAppMetrics? metrics = null,
        IManyChatAudienceResolver? manyChatAudienceResolver = null,
        IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        _userResolver = userResolver;
        _userStore = userStore;
        _graceIndexStore = graceIndexStore;
        _manyChatSync = manyChatSync;
        _manyChatDispatchQueue = manyChatDispatchQueue;
        _manyChatAudienceResolver = manyChatAudienceResolver;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
        _individualGracePolicy = individualGracePolicy;
        _priceCatalog = priceCatalog;
        _companyStore = companyStore;
        _entitlementStore = entitlementStore;
        _companyAdminInviteService = companyAdminInviteService;
        _expiryIndex = expiryIndex;
        _failedActionStore = failedActionStore;
        _stripeAdminClient = stripeAdminClient;
        _billingRecoveryNotifier = billingRecoveryNotifier;
        _adminPaymentAlerts = adminPaymentAlerts;
        _metrics = metrics;
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

    public async Task HandleCustomerUpdatedAsync(StripeEventData data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.CustomerId))
            return;

        if (data.PaymentMethodUpdated != true)
            return;

        var opWatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "stripe_customer_updated",
            ["StripeEventId"] = data.StripeEventId,
            ["StripeCustomerId"] = data.CustomerId
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} PaymentMethodUpdated={PaymentMethodUpdated}",
            "entry",
            "stripe_customer_updated",
            data.PaymentMethodUpdated);

        var resolveWatch = Stopwatch.StartNew();
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(data.CustomerId.Trim(), ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
            "dependency",
            "table_storage",
            "user_resolver.resolve_by_stripe_customer_id",
            "UserStripeCustomerLookup",
            resolveWatch.ElapsedMilliseconds,
            true,
            userRef is not null);

        if (userRef is null)
            return;

        var userWatch = Stopwatch.StartNew();
        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "user.get",
            "Users",
            userWatch.ElapsedMilliseconds,
            user is not null);

        if (user is null || !IsActivePaymentRecovery(user))
            return;

        var customerId = NullIfBlank(user.StripeCustomerId) ?? NullIfBlank(data.CustomerId);
        var subscriptionId = NullIfBlank(user.StripeSubscriptionId) ?? NullIfBlank(data.SubscriptionId);

        if (customerId is null || subscriptionId is null)
            return;

        var retryWatch = Stopwatch.StartNew();
        var attempt = await _stripeAdminClient.RetryOpenInvoiceAsync(customerId, subscriptionId, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} InvoiceId={InvoiceId} InvoiceFound={InvoiceFound}",
            "dependency",
            "stripe",
            "retry_open_invoice_after_customer_updated",
            "Stripe API",
            retryWatch.ElapsedMilliseconds,
            attempt.InvoiceFound,
            attempt.InvoiceId,
            attempt.InvoiceFound);

        if (!string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            await _billingRecoveryNotifier.NotifyRetryOutcomeAsync(user, attempt, data.StripeEventId, ct);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
            "outcome",
            "completed",
            attempt.InvoicePaid ? "invoice_retry_requested_after_payment_method_update" : "payment_method_update_processed",
            opWatch.ElapsedMilliseconds);
    }

    // =========================================================
    // CHECKOUT
    // =========================================================

    public async Task HandleCheckoutSessionCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (string.IsNullOrWhiteSpace(data.CustomerId))
            throw new ArgumentException("StripeEventData.CustomerId is required for checkout completion.");

        var opWatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "stripe_checkout_completed",
            ["StripeEventId"] = data.StripeEventId,
            ["StripeCustomerId"] = data.CustomerId,
            ["SubscriptionId"] = data.SubscriptionId
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} PlanType={PlanType} Quantity={Quantity} CheckoutMode={CheckoutMode}",
            "entry",
            "stripe_checkout_completed",
            data.Metadata is not null && data.Metadata.TryGetValue("planType", out var planTypeRaw) ? planTypeRaw : null,
            data.Quantity,
            data.CheckoutMode);

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
        var manyChatChannel = GetMeta(data, "manychatChannel");
        var phoneE164 = GetMeta(data, "phone");

        var user = await _userStore.GetOrCreateByExternalIdentityAsync(
            emailNormalized,
            phoneE164,
            string.IsNullOrWhiteSpace(manyChatSubscriberId) ? null : ExternalIdentityProviders.ManyChat,
            manyChatSubscriberId,
            manyChatChannel,
            ct);

        if (IsDuplicateByEventId(user, data.StripeEventId))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} UserId={UserId}",
                "decision",
                "duplicate_event_skipped",
                "skipped_duplicate",
                "last_stripe_event_id_matches",
                user.UserId);

            return;
        }

        if (IsOutOfOrder(user, data.StripeEventCreatedUtc))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} UserId={UserId} EventCreatedUtc={EventCreatedUtc} LastEventCreatedUtc={LastEventCreatedUtc}",
                "decision",
                "out_of_order_event_skipped",
                "skipped_out_of_order",
                "stripe_event_created_before_last_processed",
                user.UserId,
                data.StripeEventCreatedUtc,
                user.LastStripeEventCreatedUtc);

            return;
        }

        user.StripeCustomerId = data.CustomerId!.Trim();
        user.LastStripeEventId = NullIfBlank(data.StripeEventId) ?? user.LastStripeEventId;
        user.LastStripeEventCreatedUtc = data.StripeEventCreatedUtc;

        user.CohortId = string.IsNullOrWhiteSpace(cohortId) ? user.CohortId : cohortId;
        user.SchoolId = string.IsNullOrWhiteSpace(schoolId) ? user.SchoolId : schoolId;

        var priceId = NullIfBlank(data.PriceId);
        var interval = NullIfBlank(data.Interval);
        var planFromPrice = DerivePlanTypeFromPriceId(priceId);
        var planFromMeta = DerivePlanTypeFromMeta(planTypeMeta);
        var checkoutPlan = NormalizeCheckoutPlan(planFromPrice ?? planFromMeta ?? user.PlanType ?? "individual_monthly");
        var isOneTimeCheckout =
            string.Equals(data.CheckoutMode, "payment", StringComparison.OrdinalIgnoreCase)
            || checkoutPlan is "cdl_cohort" or "cdl_english_cohort";

        if (!isOneTimeCheckout)
        {
            if (!string.IsNullOrWhiteSpace(data.SubscriptionId))
                user.StripeSubscriptionId = data.SubscriptionId!.Trim();

            if (priceId is not null)
            {
                user.StripePriceId = priceId;
                user.IndividualPlanTerm = DeriveTermFromPriceId(priceId) ?? DeriveTermFromInterval(interval) ?? DeriveTermFromMeta(planFromMeta) ?? user.IndividualPlanTerm;
                user.PlanType = planFromPrice ?? planFromMeta ?? user.PlanType;
            }
            else
            {
                user.PlanType = planFromMeta ?? user.PlanType;
                user.IndividualPlanTerm = DeriveTermFromInterval(interval) ?? DeriveTermFromMeta(planFromMeta) ?? user.IndividualPlanTerm;
            }

            if (string.IsNullOrWhiteSpace(user.PlanType))
                user.PlanType = planFromMeta ?? "individual_monthly";
        }
        else if (string.IsNullOrWhiteSpace(user.PlanType))
        {
            user.PlanType = planFromPrice ?? planFromMeta ?? user.PlanType;
            user.IndividualPlanTerm = DeriveTermFromInterval(interval) ?? DeriveTermFromMeta(planFromMeta) ?? user.IndividualPlanTerm;
        }

        _logger.LogInformation("Plan determined. LogCategory={LogCategory} PriceId={PriceId} Interval={Interval} PlanFromPrice={PlanFromPrice} PlanFromMeta={PlanFromMeta} FinalPlan={FinalPlan} Term={Term}",
            "plan_determination",
            priceId,
            interval,
            planFromPrice,
            planFromMeta,
            user.PlanType,
            user.IndividualPlanTerm);

        user.UpdatedAtUtc = nowUtc;

        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        switch (checkoutPlan)
        {
            case "individual":
                {
                    // Bootstrap only. subscription.updated is the source of truth.
                    user.SubscriptionStatus = "active";
                    user.UpdatedAtUtc = nowUtc;

                    await _userStore.UpsertAsync(user, ct);
                    await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

                    _logger.LogInformation(
                        "Operation step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} UserId={UserId} PriceId={PriceId} Term={Term}",
                        "step",
                        "checkout_individual",
                        "applied",
                        user.UserId,
                        user.StripePriceId,
                        user.IndividualPlanTerm);

                    break;
                }

            case "fleet":
                {
                    if (string.IsNullOrWhiteSpace(companyId))
                        throw new InvalidOperationException("fleet checkout requires metadata companyId.");

                    var seats = data.Quantity is > 0 ? data.Quantity.Value : ParseInt(seatsMeta, 0);
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
                        SeatsTotal = seats,
                        IsOverCapacity = ent.SeatsUsed > seats,
                        StartUtc = (ent.StartUtc == default) ? nowUtc : nowUtc,
                        Status = "active",
                        UpdatedAtUtc = nowUtc,
                    };

                    await _entitlementStore.UpsertAsync(entitlement, ct);
                    await _companyAdminInviteService.EnsureActiveInviteAsync(
                        companyId!,
                        entitlement.EntitlementId,
                        seats,
                        entitlement.SeatsUsed,
                        "system:fleet_checkout_auto",
                        nowUtc,
                        ct);

                    _logger.LogInformation(
                        "Operation step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} CompanyId={CompanyId} EntitlementId={EntitlementId} Seats={Seats}",
                        "step",
                        "checkout_fleet",
                        "applied",
                        companyId,
                        entitlementId,
                        seats);

                    break;
                }

            case "cdl_cohort":
                {
                    if (string.IsNullOrWhiteSpace(companyId))
                        throw new InvalidOperationException("cdl_cohort checkout requires metadata companyId.");

                    var seats = ParseInt(seatsMeta, data.Quantity ?? 0);
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
                        "Operation step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} CompanyId={CompanyId} EntitlementId={EntitlementId} Seats={Seats} EndUtc={EndUtc}",
                        "step",
                        "checkout_cdl_cohort",
                        "applied",
                        companyId,
                        entitlementId,
                        seats,
                        ent.EndUtc);

                    break;
                }

            case "cdl_english_cohort":
                {
                    user.CohortAccessGrantedAtUtc ??= nowUtc;
                    user.UpdatedAtUtc = nowUtc;

                    await _userStore.UpsertAsync(user, ct);
                    await _userStore.UpsertLookupsAsync(user, ct);
                    await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: true, ct);

                    _logger.LogInformation(
                        "Operation step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} UserId={UserId} CohortId={CohortId}",
                        "step",
                        "checkout_cdl_english_cohort",
                        "applied",
                        user.UserId,
                        user.CohortId);

                    break;
                }

            default:
                throw new InvalidOperationException($"Unknown normalized checkout plan '{user.PlanType}'.");
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
            "outcome",
            "completed",
            "checkout_flow_applied",
            opWatch.ElapsedMilliseconds);
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
        var opWatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "stripe_subscription_reduce",
            ["StripeEventId"] = signal.StripeEventId,
            ["StripeCustomerId"] = signal.StripeCustomerId,
            ["SubscriptionId"] = signal.StripeSubscriptionId
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} Kind={Kind} TriggerPaymentFailedFlow={TriggerPaymentFailedFlow}",
            "entry",
            "stripe_subscription_reduce",
            signal.Kind,
            triggerPaymentFailedFlowIfNeeded);

        var resolveWatch = Stopwatch.StartNew();
        var userRef = await _userResolver.ResolveByStripeCustomerIdAsync(signal.StripeCustomerId, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Found={Found}",
            "dependency",
            "table_storage",
            "user_resolver.resolve_by_stripe_customer_id",
            "UserStripeCustomerLookup",
            resolveWatch.ElapsedMilliseconds,
            true,
            userRef is not null);

        if (userRef is null)
        {
            if (signal.Kind == StripeSignalKind.InvoicePaymentFailed)
                await NotifyInvoicePaymentFailedAsync(signal, null, "user_not_found_by_stripe_customer_id", ct);

            await TryProjectCompanyEntitlementWithoutUserAsync(signal, _clock.UtcNow, ct);

            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "user_resolution",
                "no_action_needed",
                "user_not_found_by_stripe_customer_id");
            return null;
        }

        var userReadWatch = Stopwatch.StartNew();
        var user = await _userStore.GetAsync(userRef.Value.UserPk, userRef.Value.UserId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "user.get",
            "Users",
            userReadWatch.ElapsedMilliseconds,
            user is not null);

        if (user is null)
        {
            if (signal.Kind == StripeSignalKind.InvoicePaymentFailed)
                await NotifyInvoicePaymentFailedAsync(signal, null, "user_not_found_after_resolve", ct);

            await TryProjectCompanyEntitlementWithoutUserAsync(signal, _clock.UtcNow, ct);

            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} UserPk={UserPk} UserId={UserId}",
                "decision",
                "user_projection_missing",
                "no_action_needed",
                "user_not_found_after_resolve",
                userRef.Value.UserPk,
                userRef.Value.UserId);
            return null;
        }

        if (IsDuplicateByEventId(user, signal.StripeEventId))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} UserId={UserId}",
                "decision",
                "duplicate_event_skipped",
                "skipped_duplicate",
                "last_stripe_event_id_matches",
                user.UserId);

            return user.EffectiveAccess is not null
                ? new AccessDecision(
                    user.EffectiveAccess.Mode,
                    user.EffectiveAccess.Source,
                    user.EffectiveAccess.GraceEndsAtUtc,
                    "Skipped duplicate Stripe event")
                : null;
        }

        if (IsOutOfOrder(user, signal.StripeEventCreatedUtc))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} UserId={UserId} EventCreatedUtc={EventCreatedUtc} LastEventCreatedUtc={LastEventCreatedUtc}",
                "decision",
                "out_of_order_event_skipped",
                "skipped_out_of_order",
                "stripe_event_created_before_last_processed",
                user.UserId,
                signal.StripeEventCreatedUtc,
                user.LastStripeEventCreatedUtc);

            return user.EffectiveAccess is not null
                ? new AccessDecision(
                    user.EffectiveAccess.Mode,
                    user.EffectiveAccess.Source,
                    user.EffectiveAccess.GraceEndsAtUtc,
                    "Skipped out-of-order Stripe event")
                : null;
        }

        var nowUtc = _clock.UtcNow;

        // 1) Apply signal facts to user shadow cache.
        ApplySignalFacts(user, signal, nowUtc);

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} Status={Status} PriceId={PriceId} PlanType={PlanType} PeriodEndUtc={PeriodEndUtc}",
            "step",
            "apply_signal_facts",
            user.SubscriptionStatus,
            user.StripePriceId,
            user.PlanType,
            user.StripeCurrentPeriodEndUtc);

        // Keep fleet/company entitlement projection in sync with subscription lifecycle.
        var projectionWatch = Stopwatch.StartNew();
        await ProjectCompanyEntitlementFromSignalAsync(user, signal, nowUtc, ct);

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} DurationMs={DurationMs}",
            "step",
            "project_company_entitlement",
            projectionWatch.ElapsedMilliseconds);

        // 2) Build facts for reducer.
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

        // 3) Reduce.
        var reduced = SubscriptionReducer.Reduce(
            facts,
            nowUtc,
            _individualGracePolicy,
            user.IndividualGraceEndsAtUtc);

        _logger.LogInformation(
            "Decision recorded. LogCategory={LogCategory} Decision={Decision} State={State} Reason={Reason} GraceEndsAtUtc={GraceEndsAtUtc}",
            "decision",
            "subscription_reduce",
            reduced.State,
            reduced.Reason,
            reduced.GraceEndsAtUtc);

        // 4) Apply reducer result to user.
        ApplyReducerResult(user, reduced);
        var previousRecoveryStartedAtUtc = user.PaymentRecoveryStartedAtUtc;
        var paymentRecoveryJustStarted = SyncPaymentRecoveryState(user, signal, reduced, nowUtc);
        var paymentRecoveryJustEnded = previousRecoveryStartedAtUtc is not null && user.PaymentRecoveryStartedAtUtc is null;

        // 5) Maintain grace index.
        var graceWatch = Stopwatch.StartNew();
        await SyncGraceIndex(user, userRef.Value, ct);

        _logger.LogDebug(
            "Persistence step completed. LogCategory={LogCategory} Step={Step} DurationMs={DurationMs}",
            "persistence",
            "sync_grace_index",
            graceWatch.ElapsedMilliseconds);

        // 6) Recompute access + ManyChat sync (inside orchestrator).
        var accessWatch = Stopwatch.StartNew();
        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Mode={Mode} Source={Source}",
            "dependency",
            "application",
            "access_orchestrator.recompute",
            "AccessOrchestrator",
            accessWatch.ElapsedMilliseconds,
            true,
            decision.Mode,
            decision.Source);

        // 7) Persist.
        var persistWatch = Stopwatch.StartNew();
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
            "persistence",
            "user.upsert_and_lookups",
            "Users+Lookups",
            persistWatch.ElapsedMilliseconds,
            true);

        _logger.LogInformation(
            "Operation step applied. LogCategory={LogCategory} Kind={Kind} UserId={UserId} Reason={Reason} Status={Status} PeriodEndUtc={PeriodEndUtc} GraceEndUtc={GraceEndUtc}",
            "step",
            signal.Kind,
            user.UserId,
            reduced.Reason,
            user.SubscriptionStatus,
            user.StripeCurrentPeriodEndUtc,
            user.IndividualGraceEndsAtUtc);

        if (paymentRecoveryJustStarted)
        {
            await _billingRecoveryNotifier.NotifyRecoveryActiveAsync(user, signal.StripeEventId, ct);

            if (signal.Kind == StripeSignalKind.InvoicePaymentFailed)
                await NotifyInvoicePaymentFailedAsync(signal, user, "payment_recovery_started", ct);
        }

        if (paymentRecoveryJustEnded)
        {
            if (signal.Kind == StripeSignalKind.InvoicePaid)
                await _billingRecoveryNotifier.NotifyRecoveredAsync(user, previousRecoveryStartedAtUtc, signal.StripeEventId, ct);
            else
                await _billingRecoveryNotifier.NotifyClosedAsync(user, previousRecoveryStartedAtUtc, signal.StripeEventId, ct);
        }

        var paymentFailedSubscriberId = await ResolvePreferredManyChatSubscriberIdAsync(user, ct);
        if (triggerPaymentFailedFlowIfNeeded
            && paymentRecoveryJustStarted
            && !string.IsNullOrWhiteSpace(paymentFailedSubscriberId)
            && user.PaymentRecoveryStartedAtUtc is not null)
        {
            var manyChatWatch = Stopwatch.StartNew();
            var payload = JsonSerializer.Serialize(
                new ManyChatPaymentFailedFlowFailedActionPayload(
                    SubscriberId: paymentFailedSubscriberId,
                    UserId: user.UserId,
                    CompanyId: user.CompanyId,
                    SubscriptionId: user.StripeSubscriptionId,
                    RecoveryStartedAtUtc: user.PaymentRecoveryStartedAtUtc,
                    CorrelationId: signal.StripeEventId,
                    Reason: "trigger_payment_failed_flow",
                    OperationName: "manychat_trigger_payment_failed"),
                JsonOpts);
            try
            {
                if (_manyChatDispatchQueue is not null)
                {
                    await _manyChatDispatchQueue.EnqueueAsync(
                        new ManyChatDispatchMessage(
                            FailedActionRetryService.ActionManyChatPaymentFailedFlow,
                            payload,
                            signal.StripeEventId,
                            _clock.UtcNow),
                        ct);
                    _metrics?.ManyChatDispatchQueued(FailedActionRetryService.ActionManyChatPaymentFailedFlow);
                }
                else
                {
                    await _manyChatSync.TriggerPaymentFailedFlowAsync(paymentFailedSubscriberId, ct);
                }

                _logger.LogDebug(
                    "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Outcome={Outcome} DispatchMode={DispatchMode}",
                    "dependency",
                    _manyChatDispatchQueue is null ? "manychat" : "manychat_dispatch_queue",
                    _manyChatDispatchQueue is null ? "trigger_payment_failed_flow" : "enqueue_payment_failed_flow",
                    _manyChatDispatchQueue is null ? "ManyChat API" : "Azure Queue Storage",
                    manyChatWatch.ElapsedMilliseconds,
                    true,
                    "applied",
                    _manyChatDispatchQueue is null ? "direct" : "queue");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ManyChatRequestException ex) when (ex.IsRetryable)
            {
                _logger.LogWarning(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "exception",
                    "dependency_failed",
                    "manychat",
                    "trigger_payment_failed_flow",
                    "ManyChat API",
                    manyChatWatch.ElapsedMilliseconds,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);

                await _failedActionStore.EnqueueAsync(
                    FailedActionRetryService.ActionManyChatPaymentFailedFlow,
                    payload,
                    _clock.UtcNow.AddMinutes(2),
                    ct);

                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "trigger_payment_failed_flow",
                    "queued_for_retry",
                    "retryable_manychat_failure");
            }
            catch (ManyChatRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "exception",
                    "validation_failed",
                    "manychat",
                    "trigger_payment_failed_flow",
                    "ManyChat API",
                    manyChatWatch.ElapsedMilliseconds,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs}",
                    "exception",
                    "dependency_failed",
                    "manychat",
                    "trigger_payment_failed_flow",
                    "ManyChat API",
                    manyChatWatch.ElapsedMilliseconds);

                if (_manyChatDispatchQueue is not null)
                {
                    try
                    {
                        await _failedActionStore.EnqueueAsync(
                            FailedActionRetryService.ActionManyChatPaymentFailedFlow,
                            payload,
                            _clock.UtcNow.AddMinutes(2),
                            ct);
                    }
                    catch (Exception enqueueEx)
                    {
                        _logger.LogError(
                            enqueueEx,
                            "Persistence failed. LogCategory={LogCategory} Outcome={Outcome} PersistenceOperation={PersistenceOperation} Target={Target}",
                            "exception",
                            "dependency_failed",
                            "failed_action.enqueue",
                            "FailedActions");
                    }
                }
            }
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} Kind={Kind} UserId={UserId} DurationMs={DurationMs}",
            "outcome",
            "applied",
            reduced.Reason,
            signal.Kind,
            user.UserId,
            opWatch.ElapsedMilliseconds);

        return decision;
    }

    private async Task NotifyInvoicePaymentFailedAsync(
        StripeSignal signal,
        User? user,
        string reason,
        CancellationToken ct)
    {
        if (_adminPaymentAlerts is null)
            return;

        await _adminPaymentAlerts.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_invoice_payment_failed",
            FailureStage = "subscription_renewal_or_invoice_payment",
            FailureReason = reason,
            Severity = "Critical",
            OccurredAtUtc = signal.StripeEventCreatedUtc,
            UserPk = user is null ? null : Buckets.UserBucketPk(user.UserId),
            UserId = user?.UserId,
            Email = user?.EmailNormalized,
            PhoneE164 = user?.PhoneE164,
            ManyChatSubscriberId = user?.ManyChatSubscriberId,
            CompanyId = user?.CompanyId,
            StripeCustomerId = signal.StripeCustomerId,
            StripeSubscriptionId = signal.StripeSubscriptionId,
            StripeEventId = signal.StripeEventId,
            PlanType = user?.PlanType,
            PriceId = signal.PriceId,
            Details =
            {
                ["stripeStatus"] = signal.Status,
                ["stripeInterval"] = signal.Interval,
                ["quantity"] = signal.Quantity?.ToString(),
                ["currentPeriodEndUtc"] = signal.CurrentPeriodEndUtc?.UtcDateTime.ToString("O"),
                ["cancelAtPeriodEnd"] = signal.CancelAtPeriodEnd?.ToString(),
                ["localSubscriptionStatus"] = user?.SubscriptionStatus,
                ["localPlanTerm"] = user?.IndividualPlanTerm,
                ["paymentRecoveryStartedAtUtc"] = user?.PaymentRecoveryStartedAtUtc?.UtcDateTime.ToString("O")
            }
        }, ct);
    }

    private async Task<string?> ResolvePreferredManyChatSubscriberIdAsync(User user, CancellationToken ct)
    {
        if (_manyChatAudienceResolver is null)
            return string.IsNullOrWhiteSpace(user.ManyChatSubscriberId) ? null : user.ManyChatSubscriberId.Trim();

        return await _manyChatAudienceResolver.ResolvePreferredSubscriberIdAsync(user, ExternalAudiencePurposes.PaymentFailedFlow, ct);
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

    private static bool SyncPaymentRecoveryState(
        User user,
        StripeSignal signal,
        IndividualEntitlementResult reduced,
        DateTimeOffset nowUtc)
    {
        var inPaymentRecovery = IsPaymentRecoveryLifecycle(user.SubscriptionStatus, reduced.State);
        if (!inPaymentRecovery)
        {
            user.PaymentRecoveryStartedAtUtc = null;
            return false;
        }

        if (signal.Kind != StripeSignalKind.InvoicePaymentFailed)
            return false;

        if (user.PaymentRecoveryStartedAtUtc is not null)
            return false;

        user.PaymentRecoveryStartedAtUtc = nowUtc;
        return true;
    }

    private async Task ProjectCompanyEntitlementFromSignalAsync(
        User user,
        StripeSignal signal,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "project_company_entitlement",
            ["StripeEventId"] = signal.StripeEventId,
            ["StripeCustomerId"] = signal.StripeCustomerId,
            ["SubscriptionId"] = signal.StripeSubscriptionId,
            ["UserId"] = user.UserId,
            ["CompanyId"] = user.CompanyId
        });

        _logger.LogInformation(
            "Step started. LogCategory={LogCategory} Step={Step} SignalKind={SignalKind}",
            "step",
            "project_company_entitlement",
            signal.Kind);

        if (signal.Kind is not (StripeSignalKind.SubscriptionUpdated
            or StripeSignalKind.SubscriptionDeleted
            or StripeSignalKind.InvoicePaid
            or StripeSignalKind.InvoicePaymentFailed))
            return;

        if (string.IsNullOrWhiteSpace(signal.StripeSubscriptionId))
            return;

        signal = await EnrichCompanySignalFromStripeAsync(signal, ct);

        var companyId = await ResolveCompanyIdForSignalAsync(signal, user, ct);
        await ProjectCompanyEntitlementCoreAsync(companyId, signal, nowUtc, user.PlanType, ct);
    }

    private async Task TryProjectCompanyEntitlementWithoutUserAsync(
        StripeSignal signal,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdForSignalAsync(signal, user: null, ct);
        await ProjectCompanyEntitlementCoreAsync(companyId, signal, nowUtc, fallbackPlanType: null, ct);
    }

    private async Task<string?> ResolveCompanyIdForSignalAsync(StripeSignal signal, User? user, CancellationToken ct)
    {
        var companyId = NullIfBlank(user?.CompanyId);
        var customerId = NullIfBlank(user?.StripeCustomerId) ?? NullIfBlank(signal.StripeCustomerId);

        if (companyId is null && customerId is not null)
        {
            var company = await _companyStore.GetByStripeCustomerIdAsync(customerId, ct);
            companyId = company?.CompanyId;
        }

        return companyId;
    }

    private async Task ProjectCompanyEntitlementCoreAsync(
        string? companyId,
        StripeSignal signal,
        DateTimeOffset nowUtc,
        string? fallbackPlanType,
        CancellationToken ct)
    {
        if (companyId is null)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "project_company_entitlement",
                "no_action_needed",
                "company_not_resolved");
            return;
        }

        var entitlementId = ResolveEntitlementIdForFleet(companyId, signal.StripeSubscriptionId);
        var entitlement = await _entitlementStore.GetAsync(companyId, entitlementId, ct);

        var plan = DerivePlanTypeFromPriceId(signal.PriceId) ?? fallbackPlanType;
        var isFleetPlan = string.Equals(plan, "company_seat", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(plan, "fleet", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(plan, "fleet_seat", StringComparison.OrdinalIgnoreCase);

        if (!isFleetPlan && entitlement is null)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} PlanType={PlanType}",
                "decision",
                "project_company_entitlement",
                "no_action_needed",
                "not_fleet_plan_and_no_entitlement",
                plan);
            return;
        }

        if (entitlement is null)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "project_company_entitlement",
                "no_action_needed",
                "entitlement_not_found");
            return;
        }

        var previousEndUtc = entitlement.EndUtc;
        var seatsTotal = ResolveSeatsTotal(entitlement.SeatsTotal, signal.Quantity);

        if (signal.Kind == StripeSignalKind.InvoicePaymentFailed)
        {
            var projected = CreateProjectedEntitlement(
                entitlement,
                seatsTotal,
                "past_due",
                nowUtc,
                entitlement.EndUtc);

            await _entitlementStore.UpsertAsync(projected, ct);

            _logger.LogInformation(
                "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity}",
                "outcome",
                "applied",
                "company_entitlement_marked_past_due",
                entitlementId,
                projected.SeatsTotal,
                projected.SeatsUsed,
                projected.IsOverCapacity);
            return;
        }

        if (signal.Kind == StripeSignalKind.InvoicePaid)
        {
            var projected = CreateProjectedEntitlement(
                entitlement,
                seatsTotal,
                "active",
                nowUtc,
                entitlement.EndUtc);

            await _entitlementStore.UpsertAsync(projected, ct);

            _logger.LogInformation(
                "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity}",
                "outcome",
                "applied",
                "company_entitlement_marked_active",
                entitlementId,
                projected.SeatsTotal,
                projected.SeatsUsed,
                projected.IsOverCapacity);
            return;
        }

        if (signal.Kind == StripeSignalKind.SubscriptionUpdated)
        {
            var projectedStatus = ResolveProjectedCompanyEntitlementStatus(signal.Status);

            if (signal.CancelAtPeriodEnd == true && signal.CurrentPeriodEndUtc is not null)
            {
                var projectedEndUtc = signal.CurrentPeriodEndUtc.Value;

                var projected = CreateProjectedEntitlement(
                    entitlement,
                    seatsTotal,
                    projectedStatus,
                    nowUtc,
                    projectedEndUtc);

                await _entitlementStore.UpsertAsync(projected, ct);
                await DeleteExpiryIndexIfPresent(companyId, entitlementId, previousEndUtc, ct);
                await _expiryIndex.UpsertAsync(new EntitlementRef(companyId, entitlementId), projectedEndUtc, ct);

                _logger.LogInformation(
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} EndUtc={EndUtc} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity} Status={Status}",
                    "outcome",
                    "applied",
                    "projected_paid_through_end",
                    entitlementId,
                    projectedEndUtc,
                    projected.SeatsTotal,
                    projected.SeatsUsed,
                    projected.IsOverCapacity,
                    projected.Status);
                return;
            }

            if (signal.CancelAtPeriodEnd == false)
            {
                var projected = CreateProjectedEntitlement(
                    entitlement,
                    seatsTotal,
                    projectedStatus,
                    nowUtc,
                    endUtc: null);

                await _entitlementStore.UpsertAsync(projected, ct);
                await DeleteExpiryIndexIfPresent(companyId, entitlementId, previousEndUtc, ct);

                _logger.LogInformation(
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity} Status={Status}",
                    "outcome",
                    "applied",
                    "cancel_at_period_end_removed",
                    entitlementId,
                    projected.SeatsTotal,
                    projected.SeatsUsed,
                    projected.IsOverCapacity,
                    projected.Status);
                return;
            }

            var updated = CreateProjectedEntitlement(
                entitlement,
                seatsTotal,
                projectedStatus,
                nowUtc,
                entitlement.EndUtc);

            await _entitlementStore.UpsertAsync(updated, ct);

            _logger.LogInformation(
                "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity} Status={Status}",
                "outcome",
                "applied",
                "subscription_updated_projected_without_cancel_change",
                entitlementId,
                updated.SeatsTotal,
                updated.SeatsUsed,
                updated.IsOverCapacity,
                updated.Status);
            return;
        }

        var endedUtc = signal.EndedAtUtc ?? signal.CurrentPeriodEndUtc ?? nowUtc;

        if (endedUtc > nowUtc)
        {
            var paidThrough = CreateProjectedEntitlement(
                entitlement,
                seatsTotal,
                "active",
                nowUtc,
                endedUtc);

            await _entitlementStore.UpsertAsync(paidThrough, ct);
            await DeleteExpiryIndexIfPresent(companyId, entitlementId, previousEndUtc, ct);
            await _expiryIndex.UpsertAsync(new EntitlementRef(companyId, entitlementId), endedUtc, ct);

            _logger.LogInformation(
                "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} EndUtc={EndUtc} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity}",
                "outcome",
                "applied",
                "subscription_deleted_but_paid_through",
                entitlementId,
                endedUtc,
                paidThrough.SeatsTotal,
                paidThrough.SeatsUsed,
                paidThrough.IsOverCapacity);
            return;
        }

        var expired = CreateProjectedEntitlement(
            entitlement,
            seatsTotal,
            "expired",
            nowUtc,
            endedUtc);

        await _entitlementStore.UpsertAsync(expired, ct);
        await DeleteExpiryIndexIfPresent(companyId, entitlementId, previousEndUtc, ct);

        _logger.LogInformation(
            "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} EntitlementId={EntitlementId} EndUtc={EndUtc} SeatsTotal={SeatsTotal} SeatsUsed={SeatsUsed} IsOverCapacity={IsOverCapacity}",
            "outcome",
            "expired",
            "company_entitlement_expired",
            entitlementId,
            endedUtc,
            expired.SeatsTotal,
            expired.SeatsUsed,
            expired.IsOverCapacity);
    }

    private async Task DeleteExpiryIndexIfPresent(
        string companyId,
        string entitlementId,
        DateTimeOffset? endUtc,
        CancellationToken ct)
    {
        if (endUtc is null)
            return;

        var pk = $"{TablePrefixes.EntitlementExpiry}_{endUtc.Value:yyyyMMdd}";
        var rk = $"{endUtc.Value.Ticks:D19}_{companyId}_{entitlementId}";

        await _expiryIndex.DeleteAsync(pk, rk, ct);
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

    private static bool IsDuplicateByEventId(User user, string? stripeEventId)
        => !string.IsNullOrWhiteSpace(stripeEventId)
           && !string.IsNullOrWhiteSpace(user.LastStripeEventId)
           && string.Equals(
                stripeEventId.Trim(),
                user.LastStripeEventId.Trim(),
                StringComparison.OrdinalIgnoreCase);
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

    private static bool IsPaymentRecoveryLifecycle(string? status, IndividualEntitlementState reducedState)
        => IsDelinquentStatus(status)
           && (reducedState is IndividualEntitlementState.Grace or IndividualEntitlementState.Blocked);

    private static bool IsDelinquentStatus(string? status)
    {
        var normalized = NormalizeStatus(status);
        return normalized is "past_due" or "payment_failed" or "unpaid" or "incomplete" or "incomplete_expired";
    }

    private static string ResolveProjectedCompanyEntitlementStatus(string? status)
        => IsDelinquentStatus(status) ? "past_due" : "active";

    private static int ResolveSeatsTotal(int existingSeatsTotal, int? quantity)
        => quantity.HasValue ? Math.Max(quantity.Value, 0) : existingSeatsTotal;

    private static Entitlement CreateProjectedEntitlement(
        Entitlement entitlement,
        int seatsTotal,
        string status,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? endUtc)
    {
        var normalizedSeatsTotal = Math.Max(seatsTotal, 0);

        return new Entitlement
        {
            EntitlementId = entitlement.EntitlementId,
            CompanyId = entitlement.CompanyId,
            SeatsUsed = entitlement.SeatsUsed,
            SeatsTotal = normalizedSeatsTotal,
            IsOverCapacity = entitlement.SeatsUsed > normalizedSeatsTotal,
            StartUtc = entitlement.StartUtc,
            Status = status,
            UpdatedAtUtc = updatedAtUtc,
            EndUtc = endUtc
        };
    }

    private static bool IsActivePaymentRecovery(User user)
        => user.PaymentRecoveryStartedAtUtc is not null && IsDelinquentStatus(user.SubscriptionStatus);

    private async Task<StripeSignal> EnrichCompanySignalFromStripeAsync(StripeSignal signal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(signal.StripeSubscriptionId))
            return signal;

        var needsSnapshot =
            signal.Quantity is null
            || string.IsNullOrWhiteSpace(signal.PriceId)
            || string.IsNullOrWhiteSpace(signal.Interval)
            || (signal.Kind == StripeSignalKind.SubscriptionUpdated && signal.CancelAtPeriodEnd is null)
            || (signal.Kind is StripeSignalKind.SubscriptionUpdated or StripeSignalKind.SubscriptionDeleted && string.IsNullOrWhiteSpace(signal.Status));

        if (!needsSnapshot)
            return signal;

        var snapshot = await _stripeAdminClient.GetSubscriptionAsync(signal.StripeSubscriptionId!, ct);
        if (snapshot is null)
            return signal;

        return signal with
        {
            StripeCustomerId = string.IsNullOrWhiteSpace(signal.StripeCustomerId) ? snapshot.CustomerId : signal.StripeCustomerId,
            Status = string.IsNullOrWhiteSpace(signal.Status) ? snapshot.Status : signal.Status,
            PriceId = string.IsNullOrWhiteSpace(signal.PriceId) ? snapshot.PriceId : signal.PriceId,
            Interval = string.IsNullOrWhiteSpace(signal.Interval) ? snapshot.Interval : signal.Interval,
            Quantity = signal.Quantity ?? snapshot.Quantity,
            CancelAtPeriodEnd = signal.CancelAtPeriodEnd ?? snapshot.CancelAtPeriodEnd,
            CurrentPeriodEndUtc = signal.CurrentPeriodEndUtc ?? snapshot.CurrentPeriodEndUtc,
            CanceledAtUtc = signal.CanceledAtUtc ?? snapshot.CanceledAtUtc,
            EndedAtUtc = signal.EndedAtUtc ?? snapshot.EndedAtUtc
        };
    }

    private static string NormalizeCheckoutPlan(string? planType)
    {
        var p = (planType ?? "").Trim().ToLowerInvariant();

        return p switch
        {
            "individual" or "individual_monthly" or "individual_yearly" => "individual",
            "fleet" or "fleet_seat" or "company_seat" => "fleet",
            "cdl_cohort" => "cdl_cohort",
            "cdl_english_cohort" => "cdl_english_cohort",
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

    private static string? DeriveTermFromMeta(string? planTypeMeta)
    {
        var p = (planTypeMeta ?? "").Trim().ToLowerInvariant();

        return p switch
        {
            "individual_monthly" => "monthly",
            "individual_yearly" => "yearly",
            "annual" => "yearly",
            "monthly" => "monthly",
            "fleet" => "company_seat",
            "fleet_seat" => "company_seat",
            "company_seat" => "company_seat",
            "cdl_cohort" => "cdl_cohort",
            "cdl_english_cohort" => "cdl_english_cohort",
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
        if (priceId == _priceCatalog.CdlEnglishCohortPriceId)
            return "cdl_english_cohort";

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
            "cdl_english_cohort" => "cdl_english_cohort",
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
        Quantity: i.Quantity,
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
        Quantity: i.Quantity,
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
        Quantity: i.Quantity,
        CancelAtPeriodEnd: null,
        CurrentPeriodEndUtc: i.CurrentPeriodEndUtc,
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
        Quantity: i.Quantity,
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
        Interval: d.Interval,
        Quantity: d.Quantity
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
        Interval: d.Interval,
        Quantity: d.Quantity
    );

    private static StripeInvoicePaid ToInvoicePaidDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "",
        PriceId: d.PriceId,
        Interval: d.Interval,
        CurrentPeriodEndUtc: d.CurrentPeriodEndUtc,
        Quantity: d.Quantity
    );

    private static StripeInvoicePaymentFailed ToInvoiceFailedDto(StripeEventData d) => new(
        StripeEventId: d.StripeEventId,
        StripeEventCreatedUtc: d.StripeEventCreatedUtc,
        StripeCustomerId: d.CustomerId ?? "",
        StripeSubscriptionId: d.SubscriptionId ?? "",
        PriceId: d.PriceId,
        Interval: d.Interval,
        Quantity: d.Quantity
    );
}
