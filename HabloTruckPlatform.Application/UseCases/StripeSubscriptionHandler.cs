using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.Stripex;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;

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

    public StripeSubscriptionHandler(
        IUserResolver userResolver,
        IUserStore userStore,
        IGraceIndexStore graceIndexStore,
        IManyChatSync manyChatSync,
        AccessOrchestrator accessOrchestrator,
        IClock clock,
        GracePolicy individualGracePolicy)
    {
        _userResolver = userResolver;
        _userStore = userStore;
        _graceIndexStore = graceIndexStore;
        _manyChatSync = manyChatSync;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
        _individualGracePolicy = individualGracePolicy;
    }

    // =========================================================
    // WEBHOOK-FRIENDLY METHODS (StripeEventData)
    // =========================================================

    public async Task HandleCheckoutCompletedAsync(StripeEventData data, CancellationToken ct = default)
    {
        // If you already handle checkout in CheckoutSessionHandler, you can forward there instead.
        // For now, keep it minimal: upsert lookups if we can resolve user by email or ManyChat.
        // (You can remove this if checkout is handled elsewhere.)
        if (data is null) return;

        // Optional: if you do company-pack checkout, you probably handle it in CheckoutSessionHandler.
        // Keep as no-op if you want.
        await Task.CompletedTask;
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
}