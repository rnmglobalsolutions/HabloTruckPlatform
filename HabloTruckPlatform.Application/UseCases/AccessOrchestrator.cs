

using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;

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

        await SafeManyChatSync(user, decision, ct);

        return decision;
    }

    private async Task SafeManyChatSync(User user, AccessDecision decision, CancellationToken ct)
    {
        // If there is no subscriber id, we can't sync.
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            return;

        try
        {
            await _manyChatSync.SyncUserAccessAsync(user, decision, ct);
        }
        catch (Exception ex)
        {
            // enqueue for retry
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                userPk = HabloTruckPlatform.Domain.Ids.Buckets.UserBucketPk(user.UserId),
                userId = user.UserId,
                reason = "sync_access"
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

            await _failedActionStore.EnqueueAsync(
                FailedActionRetryService.ActionManyChatSync,
                payload,
                nextRetryUtc: DateTimeOffset.UtcNow.AddMinutes(2),
                ct);
        }
    }
}