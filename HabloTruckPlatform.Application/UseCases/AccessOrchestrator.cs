
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
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
    private readonly IManyChatDispatchQueue? _manyChatDispatchQueue;
    private readonly IManyChatAudienceResolver? _manyChatAudienceResolver;
    private readonly IFailedActionStore _failedActionStore;
    private readonly IClock _clock;
    private readonly CompanyGracePolicy _companyGracePolicy;
    private readonly IAppMetrics? _metrics;
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
        ILogger<AccessOrchestrator>? logger = null,
        IManyChatDispatchQueue? manyChatDispatchQueue = null,
        IAppMetrics? metrics = null,
        IManyChatAudienceResolver? manyChatAudienceResolver = null)
    {
        _userStore = userStore;
        _seatStore = seatStore;
        _entitlementStore = entitlementStore;
        _manyChatSync = manyChatSync;
        _manyChatDispatchQueue = manyChatDispatchQueue;
        _manyChatAudienceResolver = manyChatAudienceResolver;
        _failedActionStore = failedActionStore;
        _clock = clock;
        _companyGracePolicy = companyGracePolicy;
        _metrics = metrics;
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
        var subscriberIds = await ResolveStateSyncSubscriberIdsAsync(user, ct);
        if (subscriberIds.Count == 0)
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                "decision",
                "manychat_sync_skipped",
                "no_action_needed",
                "missing_manychat_audience");
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

        var allHandled = true;

        foreach (var subscriberId in subscriberIds)
        {
            var targetUser = CloneForSubscriber(user, subscriberId);
            var dependencyWatch = Stopwatch.StartNew();
            var payload = JsonSerializer.Serialize(
                new ManyChatSyncFailedActionPayload(
                    UserPk: Buckets.UserBucketPk(user.UserId),
                    UserId: user.UserId,
                    SubscriberId: subscriberId,
                    CompanyId: user.CompanyId,
                    CorrelationId: user.UserId,
                    Reason: "sync_access",
                    OperationName: "manychat_sync_user_access"),
                JsonOpts);

            try
            {
                if (_manyChatDispatchQueue is not null)
                {
                    _logger.LogInformation("Sending ManyChat sync to dispatch queue. UserId={UserId} SubscriberId={SubscriberId}", user.UserId, subscriberId);
                    await _manyChatDispatchQueue.EnqueueAsync(
                        new ManyChatDispatchMessage(
                            FailedActionRetryService.ActionManyChatSync,
                            payload,
                            user.UserId,
                            _clock.UtcNow),
                        ct);
                    _logger.LogInformation("Enqueued ManyChat sync message. UserId={UserId} SubscriberId={SubscriberId}", user.UserId, subscriberId);
                    _metrics?.ManyChatDispatchQueued(FailedActionRetryService.ActionManyChatSync);

                    _logger.LogDebug(
                        "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                        "dependency",
                        "manychat_dispatch_queue",
                        "enqueue_sync_user_access",
                        "Azure Queue Storage",
                        dependencyWatch.ElapsedMilliseconds,
                        true);

                    continue;
                }

                await _manyChatSync.SyncUserAccessAsync(targetUser, decision, ct);
                _logger.LogInformation("ManyChat sync completed in direct connection to ManyChat. UserId={UserId} SubscriberId={SubscriberId}", user.UserId, subscriberId);

                _logger.LogDebug(
                    "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                    "dependency",
                    "manychat",
                    "sync_user_access",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds,
                    true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Operation cancelled. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason}",
                    "warning",
                    "operation_cancelled",
                    "cancellation_requested");
                throw;
            }
            catch (ManyChatRequestException ex) when (ex.IsRetryable)
            {
                _logger.LogWarning(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "warning: ex is retryable",
                    "dependency_failed",
                    "manychat",
                    "sync_user_access",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);

                try
                {
                    await _failedActionStore.EnqueueAsync(
                        FailedActionRetryService.ActionManyChatSync,
                        payload,
                        nextRetryUtc: _clock.UtcNow.AddMinutes(2),
                        ct);
                }
                catch (Exception enqueueEx)
                {
                    allHandled = false;
                    _logger.LogError(
                        enqueueEx,
                        "Persistence failed. LogCategory={LogCategory} Outcome={Outcome} PersistenceOperation={PersistenceOperation} Target={Target}",
                        "exception",
                        "dependency_failed",
                        "failed_action.enqueue",
                        "FailedActions");
                }
            }
            catch (ManyChatRequestException ex)
            {
                allHandled = false;
                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                    "exception",
                    "validation_failed",
                    "manychat",
                    "sync_user_access",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds,
                    ex.IsRetryable,
                    ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                    ex.FailureCategory);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs}",
                    "exception",
                    "dependency_failed",
                    "manychat",
                    "sync_user_access",
                    "ManyChat API",
                    dependencyWatch.ElapsedMilliseconds);

                if (_manyChatDispatchQueue is not null)
                {
                    try
                    {
                        await _failedActionStore.EnqueueAsync(
                            FailedActionRetryService.ActionManyChatSync,
                            payload,
                            nextRetryUtc: _clock.UtcNow.AddMinutes(2),
                            ct);
                    }
                    catch (Exception enqueueEx)
                    {
                        allHandled = false;
                        _logger.LogError(
                            enqueueEx,
                            "Persistence failed. LogCategory={LogCategory} Outcome={Outcome} PersistenceOperation={PersistenceOperation} Target={Target}",
                            "exception",
                            "dependency_failed",
                            "failed_action.enqueue",
                            "FailedActions");
                    }
                }
                else
                {
                    allHandled = false;
                }
            }
        }

        if (!allHandled)
            return;

        // Update sync watermark so we do not spam on next recompute.
        user.LastSyncedAccessMode = decision.Mode.ToString();
        user.LastSyncedAccessSource = (int)decision.Source;
        user.LastSyncedGraceEndsAtUtc = decision.GraceEndsAtUtc;
        user.LastManyChatSyncAtUtc = _clock.UtcNow;

        if (persistUser)
        {
            var persistWatch = Stopwatch.StartNew();
            await _userStore.UpsertAsync(user, ct);

            _logger.LogInformation(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "user.upsert_manychat_watermark",
                "Users",
                persistWatch.ElapsedMilliseconds,
                true);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveStateSyncSubscriberIdsAsync(User user, CancellationToken ct)
    {
        if (_manyChatAudienceResolver is null)
        {
            return string.IsNullOrWhiteSpace(user.ManyChatSubscriberId)
                ? []
                : [user.ManyChatSubscriberId.Trim()];
        }

        return await _manyChatAudienceResolver.ResolveStateSyncSubscriberIdsAsync(user, ct);
    }

    private static User CloneForSubscriber(User user, string subscriberId)
        => new()
        {
            UserId = user.UserId,
            EmailNormalized = user.EmailNormalized,
            ManyChatSubscriberId = subscriberId,
            PhoneE164 = user.PhoneE164,
            StripeCustomerId = user.StripeCustomerId,
            StripeSubscriptionId = user.StripeSubscriptionId,
            SubscriptionStatus = user.SubscriptionStatus,
            IndividualGraceEndsAtUtc = user.IndividualGraceEndsAtUtc,
            CompanyId = user.CompanyId,
            SeatEntitlementId = user.SeatEntitlementId,
            SeatStatus = user.SeatStatus,
            EffectiveAccess = user.EffectiveAccess,
            LastStripeEventId = user.LastStripeEventId,
            LastStripeEventCreatedUtc = user.LastStripeEventCreatedUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            CurrentGracePk = user.CurrentGracePk,
            CurrentGraceRk = user.CurrentGraceRk,
            StripePriceId = user.StripePriceId,
            IndividualPlanTerm = user.IndividualPlanTerm,
            StripeCurrentPeriodEndUtc = user.StripeCurrentPeriodEndUtc,
            StripeCancelAtPeriodEnd = user.StripeCancelAtPeriodEnd,
            PaymentRecoveryStartedAtUtc = user.PaymentRecoveryStartedAtUtc,
            LastSyncedAccessMode = user.LastSyncedAccessMode,
            LastSyncedAccessSource = user.LastSyncedAccessSource,
            LastSyncedGraceEndsAtUtc = user.LastSyncedGraceEndsAtUtc,
            LastManyChatSyncAtUtc = user.LastManyChatSyncAtUtc,
            PlanType = user.PlanType,
            CohortId = user.CohortId,
            SchoolId = user.SchoolId,
            CohortAccessGrantedAtUtc = user.CohortAccessGrantedAtUtc
        };

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
