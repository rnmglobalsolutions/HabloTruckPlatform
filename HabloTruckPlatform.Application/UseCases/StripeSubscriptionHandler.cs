using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Handles Stripe subscription/invoice signals (already parsed into DTOs).
/// No Stripe SDK types here.
/// </summary>
public sealed class StripeSubscriptionHandler
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

    // ----------------------------
    // customer.subscription.updated
    // ----------------------------
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

        // Update Stripe facts
        user.StripeCustomerId = input.StripeCustomerId;
        user.StripeSubscriptionId = input.StripeSubscriptionId;

        // Apply domain subscription transition (sets IndividualGraceEndsAtUtc when needed)
        SubscriptionState.ApplyStripeStatus(
            user,
            newStatus: input.SubscriptionStatus,
            nowUtc: _clock.UtcNow,
            gracePolicy: _individualGracePolicy);

        // Audit
        user.LastStripeEventId = input.StripeEventId;
        user.LastStripeEventCreatedUtc = input.StripeEventCreatedUtc;
        user.UpdatedAtUtc = _clock.UtcNow;

        // Maintain GraceIndex
        await SyncGraceIndex(user, userRef.Value, ct);

        var decision = await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        // Persist user + lookups
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        // Recompute final access (includes company seat logic)
        return decision;
    }

    // ----------------------------
    // customer.subscription.deleted
    // ----------------------------
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

        // Treat deletion as grace-worthy terminal status
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

    // ----------------------------
    // invoice.paid
    // ----------------------------
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

        // Fast restore signal (subscription.updated should also arrive, but this reduces lag)
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

    // ----------------------------
    // invoice.payment_failed
    // ----------------------------
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

        // Start/extend individual grace
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

        // Trigger recovery flow (best effort)
        if (!string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
        {
            try
            {
                await _manyChatSync.TriggerPaymentFailedFlowAsync(user.ManyChatSubscriberId!, ct);
            }
            catch
            {
                // ignore; outbox/retry can handle later via ManyChatSync implementation
            }
        }

        return decision;
    }

    // ----------------------------
    // Helpers
    // ----------------------------
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
            // clear pointers (will be persisted by caller once)
            var pk = user.CurrentGracePk;
            var rk = user.CurrentGraceRk;

            user.CurrentGracePk = null;
            user.CurrentGraceRk = null;

            // O(1) delete if we have pointers
            if (!string.IsNullOrWhiteSpace(pk) && !string.IsNullOrWhiteSpace(rk))
            {
                await _graceIndexStore.DeleteAsync(pk!, rk!, ct);
            }
            else
            {
                // fallback (if you keep it)
                await _graceIndexStore.DeleteForUserAsync(userRef, ct);
            }
        }
    }
}