

using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using System.Text.Json;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Central use case: loads access context, computes AccessDecision using Domain rules,
/// persists snapshots, and syncs ManyChat (best-effort).
/// </summary>
public sealed class AccessOrchestrator
{
    private readonly IUserStore _userStore;
    private readonly ISeatAssignmentStore _seatStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly IManyChatSync _manyChatSync;
    private readonly IFailedActionStore _failedActionStore;
    private readonly IClock _clock;
    private readonly CompanyGracePolicy _companyGracePolicy;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public AccessOrchestrator(
        IUserStore userStore,
        ISeatAssignmentStore seatStore,
        IEntitlementStore entitlementStore,
        IManyChatSync manyChatSync,
        IFailedActionStore failedActionStore,
        IClock clock,
        CompanyGracePolicy companyGracePolicy)
    {
        _userStore = userStore;
        _seatStore = seatStore;
        _entitlementStore = entitlementStore;
        _manyChatSync = manyChatSync;
        _failedActionStore = failedActionStore;
        _clock = clock;
        _companyGracePolicy = companyGracePolicy;
    }

    /// <summary>
    /// Compute and persist effective access for a user (and sync ManyChat).
    /// Call this after ANY event that can affect access:
    /// - subscription updated/deleted
    /// - invoice paid/failed
    /// - company join / seat changes
    /// - grace sweeper / entitlement sweeper
    /// </summary>
    public async Task<AccessDecision> RecomputeForUserAsync(
        string userPk,
        string userId,
        bool persistUser = true,
        CancellationToken ct = default)
    {
        var user = await _userStore.GetAsync(userPk, userId, ct)
                   ?? throw new InvalidOperationException($"User not found: {userPk}/{userId}");

        return await RecomputeForUserAsync(user, persistUser, ct);
    }

    public async Task<AccessDecision> RecomputeForUserAsync(
        User user,
        bool persistUser = true,
        CancellationToken ct = default)
    {
        var nowUtc = _clock.UtcNow;

        SeatAssignment? seat = null;
        Entitlement? entitlement = null;

        if (!string.IsNullOrWhiteSpace(user.CompanyId))
        {
            seat = await _seatStore.GetAsync(user.CompanyId!, user.UserId, ct);

            if (seat is not null && !string.IsNullOrWhiteSpace(seat.EntitlementId))
                entitlement = await _entitlementStore.GetAsync(seat.CompanyId, seat.EntitlementId, ct);
        }

        var ctx = new UserAccessContext(user, seat, entitlement);
        var decision = AccessRules.Decide(ctx, nowUtc, _companyGracePolicy);

        user.EffectiveAccess = AccessSnapshot.FromDecision(decision);
        user.UpdatedAtUtc = nowUtc;

        if (persistUser)
            await _userStore.UpsertAsync(user, ct);

        await SafeManyChatSync(user, decision, persistUser, ct);

        return decision;
    }

    private async Task SafeManyChatSync(User user, AccessDecision decision, bool persistUser, CancellationToken ct)
    {
        // If there is no subscriber id, we can't sync.
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            return;

        // (3) Dedupe: only sync if decision materially changed
        if (!ShouldSyncManyChat(user, decision))
            return;

        try
        {
            await _manyChatSync.SyncUserAccessAsync(user, decision, ct);

            // Update sync watermark so we don't spam on next recompute
            user.LastSyncedAccessMode = decision.Mode.ToString();
            user.LastSyncedAccessSource = (int)decision.Source;
            user.LastSyncedGraceEndsAtUtc = decision.GraceEndsAtUtc;
            user.LastManyChatSyncAtUtc = _clock.UtcNow;

            // Persist the watermark (only if caller wanted persistence; otherwise still safe to persist)
            if (persistUser)
                await _userStore.UpsertAsync(user, ct);
        }
        catch (Exception)
        {
            // (4) enqueue for retry with subscriberId + companyId included
            var payload = JsonSerializer.Serialize(new
            {
                userPk = Buckets.UserBucketPk(user.UserId),
                userId = user.UserId,
                subscriberId = user.ManyChatSubscriberId,
                companyId = user.CompanyId,
                reason = "sync_access"
            }, JsonOpts);

            await _failedActionStore.EnqueueAsync(
                FailedActionRetryService.ActionManyChatSync,
                payload,
                nextRetryUtc: _clock.UtcNow.AddMinutes(2),
                ct);
        }
    }

    private static bool ShouldSyncManyChat(User user, AccessDecision decision)
    {
        // If we've never synced, sync now
        if (string.IsNullOrWhiteSpace(user.LastSyncedAccessMode))
            return true;

        // Compare mode
        if (!string.Equals(user.LastSyncedAccessMode, decision.Mode.ToString(), StringComparison.Ordinal))
            return true;

        // Compare source flags
        if (user.LastSyncedAccessSource != (int)decision.Source)
            return true;

        // Compare grace end timestamp (nullable)
        var prevGrace = user.LastSyncedGraceEndsAtUtc;
        var nextGrace = decision.GraceEndsAtUtc;

        if (prevGrace is null && nextGrace is not null) return true;
        if (prevGrace is not null && nextGrace is null) return true;
        if (prevGrace is not null && nextGrace is not null && prevGrace.Value != nextGrace.Value) return true;

        return false;
    }
}