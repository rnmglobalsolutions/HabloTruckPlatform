
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text.Json;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Central use case: loads access context, computes AccessDecision using Domain rules,
/// persists snapshots, and syncs ManyChat (best-effort).
/// </summary>
public sealed class AccessOrchestrator
{
    private const string OperationName = "access_recompute";

    private readonly IUserStore _userStore;
    private readonly ISeatAssignmentStore _seatStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly IManyChatSync _manyChatSync;
    private readonly IFailedActionStore _failedActionStore;
    private readonly IClock _clock;
    private readonly CompanyGracePolicy _companyGracePolicy;
    private readonly ILogger<AccessOrchestrator> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public AccessOrchestrator(
        IUserStore userStore,
        ISeatAssignmentStore seatStore,
        IEntitlementStore entitlementStore,
        IManyChatSync manyChatSync,
        IFailedActionStore failedActionStore,
        IClock clock,
        CompanyGracePolicy companyGracePolicy,
        ILogger<AccessOrchestrator>? logger = null)
    {
        _userStore = userStore;
        _seatStore = seatStore;
        _entitlementStore = entitlementStore;
        _manyChatSync = manyChatSync;
        _failedActionStore = failedActionStore;
        _clock = clock;
        _companyGracePolicy = companyGracePolicy;
        _logger = logger ?? NullLogger<AccessOrchestrator>.Instance;
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
        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} Step={Step} UserPk={UserPk} UserId={UserId} PersistUser={PersistUser}",
            "entry",
            OperationName,
            "load_user",
            userPk,
            userId,
            persistUser);

        var lookupWatch = Stopwatch.StartNew();
        var user = await _userStore.GetAsync(userPk, userId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "user.get",
            "Users",
            lookupWatch.ElapsedMilliseconds,
            user is not null);

        if (user is null)
        {
            _logger.LogWarning(
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} UserPk={UserPk} UserId={UserId} DurationMs={DurationMs}",
                "outcome",
                "validation_failed",
                "user_not_found",
                userPk,
                userId,
                opWatch.ElapsedMilliseconds);

            throw new InvalidOperationException($"User not found: {userPk}/{userId}");
        }

        var decision = await RecomputeForUserAsync(user, persistUser, ct);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} UserId={UserId} Mode={Mode} Source={Source} DurationMs={DurationMs}",
            "outcome",
            "completed",
            "recompute_finished",
            userId,
            decision.Mode,
            decision.Source,
            opWatch.ElapsedMilliseconds);

        return decision;
    }

    public async Task<AccessDecision> RecomputeForUserAsync(
        User user,
        bool persistUser = true,
        CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = OperationName,
            ["UserId"] = user.UserId,
            ["CompanyId"] = user.CompanyId,
            ["EntitlementId"] = user.SeatEntitlementId
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} PersistUser={PersistUser}",
            "entry",
            OperationName,
            persistUser);

        var nowUtc = _clock.UtcNow;

        SeatAssignment? seat = null;
        Entitlement? entitlement = null;

        if (!string.IsNullOrWhiteSpace(user.CompanyId))
        {
            var seatWatch = Stopwatch.StartNew();
            seat = await _seatStore.GetAsync(user.CompanyId!, user.UserId, ct);

            _logger.LogDebug(
                "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
                "persistence",
                "seat_assignment.get",
                "Seats",
                seatWatch.ElapsedMilliseconds,
                seat is not null);

            if (seat is not null && !string.IsNullOrWhiteSpace(seat.EntitlementId))
            {
                var entitlementWatch = Stopwatch.StartNew();
                entitlement = await _entitlementStore.GetAsync(seat.CompanyId, seat.EntitlementId, ct);

                _logger.LogDebug(
                    "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
                    "persistence",
                    "entitlement.get",
                    "Entitlements",
                    entitlementWatch.ElapsedMilliseconds,
                    entitlement is not null);
            }
        }

        var ctx = new UserAccessContext(user, seat, entitlement);
        var decision = AccessRules.Decide(ctx, nowUtc, _companyGracePolicy);

        _logger.LogInformation(
            "Decision recorded. LogCategory={LogCategory} Decision={Decision} Reason={Reason} Mode={Mode} Source={Source} GraceEndsAtUtc={GraceEndsAtUtc}",
            "decision",
            "access_rules_decide",
            decision.Reason,
            decision.Mode,
            decision.Source,
            decision.GraceEndsAtUtc);

        user.EffectiveAccess = AccessSnapshot.FromDecision(decision);
        user.UpdatedAtUtc = nowUtc;

        if (persistUser)
        {
            var persistWatch = Stopwatch.StartNew();
            await _userStore.UpsertAsync(user, ct);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "user.upsert",
                "Users",
                persistWatch.ElapsedMilliseconds,
                true);
        }

        await SafeManyChatSync(user, decision, persistUser, ct);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} Mode={Mode} Source={Source} DurationMs={DurationMs}",
            "outcome",
            "completed",
            "access_snapshot_applied",
            decision.Mode,
            decision.Source,
            opWatch.ElapsedMilliseconds);

        return decision;
    }

    private async Task SafeManyChatSync(User user, AccessDecision decision, bool persistUser, CancellationToken ct)
    {
        // If there is no subscriber id, we cannot sync.
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "manychat_sync_skipped",
                "no_action_needed",
                "missing_subscriber_id");
            return;
        }

        // Dedupe: only sync if decision materially changed.
        if (!ShouldSyncManyChat(user, decision))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "manychat_sync_skipped",
                "no_action_needed",
                "access_state_unchanged");
            return;
        }

        var dependencyWatch = Stopwatch.StartNew();

        try
        {
            await _manyChatSync.SyncUserAccessAsync(user, decision, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "manychat",
                "sync_user_access",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds,
                true);

            // Update sync watermark so we do not spam on next recompute.
            user.LastSyncedAccessMode = decision.Mode.ToString();
            user.LastSyncedAccessSource = (int)decision.Source;
            user.LastSyncedGraceEndsAtUtc = decision.GraceEndsAtUtc;
            user.LastManyChatSyncAtUtc = _clock.UtcNow;

            if (persistUser)
            {
                var persistWatch = Stopwatch.StartNew();
                await _userStore.UpsertAsync(user, ct);

                _logger.LogDebug(
                    "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                    "persistence",
                    "user.upsert_manychat_watermark",
                    "Users",
                    persistWatch.ElapsedMilliseconds,
                    true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs}",
                "exception",
                "dependency_failed",
                "manychat",
                "sync_user_access",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds);

            var payload = JsonSerializer.Serialize(new
            {
                userPk = Buckets.UserBucketPk(user.UserId),
                userId = user.UserId,
                subscriberId = user.ManyChatSubscriberId,
                companyId = user.CompanyId,
                reason = "sync_access"
            }, JsonOpts);

            var queueWatch = Stopwatch.StartNew();
            await _failedActionStore.EnqueueAsync(
                FailedActionRetryService.ActionManyChatSync,
                payload,
                nextRetryUtc: _clock.UtcNow.AddMinutes(2),
                ct);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success} Outcome={Outcome}",
                "persistence",
                "failed_action.enqueue",
                "FailedActions",
                queueWatch.ElapsedMilliseconds,
                true,
                "dependency_failed");
        }
    }

    private static bool ShouldSyncManyChat(User user, AccessDecision decision)
    {
        // If we have never synced, sync now.
        if (string.IsNullOrWhiteSpace(user.LastSyncedAccessMode))
            return true;

        // Compare mode.
        if (!string.Equals(user.LastSyncedAccessMode, decision.Mode.ToString(), StringComparison.Ordinal))
            return true;

        // Compare source flags.
        if (user.LastSyncedAccessSource != (int)decision.Source)
            return true;

        // Compare grace end timestamp (nullable).
        var prevGrace = user.LastSyncedGraceEndsAtUtc;
        var nextGrace = decision.GraceEndsAtUtc;

        if (prevGrace is null && nextGrace is not null) return true;
        if (prevGrace is not null && nextGrace is null) return true;
        if (prevGrace is not null && nextGrace is not null && prevGrace.Value != nextGrace.Value) return true;

        return false;
    }
}

