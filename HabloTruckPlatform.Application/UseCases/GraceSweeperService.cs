using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Sweeps expired individual grace windows (hourly bucketing).
/// Your Timer Function should call this.
/// </summary>
public sealed class GraceSweeperService
{
    private readonly IGraceIndexStore _graceIndexStore;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly IClock _clock;
    private readonly ILogger<GraceSweeperService> _logger;

    public GraceSweeperService(
        IGraceIndexStore graceIndexStore,
        AccessOrchestrator accessOrchestrator,
        IClock clock,
        ILogger<GraceSweeperService>? logger = null)
    {
        _graceIndexStore = graceIndexStore;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
        _logger = logger ?? NullLogger<GraceSweeperService>.Instance;
    }

    /// <summary>
    /// Sweeps grace buckets for a rolling window.
    /// Recommended:
    /// - lookbackHours: 96 (covers 72h grace + buffer)
    /// - takePerBucket: 500 (batch)
    /// </summary>
    public async Task SweepExpiredGraceAsync(
        int lookbackHours = 96,
        int takePerBucket = 500,
        CancellationToken ct = default)
    {
        if (lookbackHours <= 0) throw new ArgumentOutOfRangeException(nameof(lookbackHours));
        if (takePerBucket <= 0) throw new ArgumentOutOfRangeException(nameof(takePerBucket));

        var opWatch = Stopwatch.StartNew();
        var nowUtc = _clock.UtcNow;

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} LookbackHours={LookbackHours} TakePerBucket={TakePerBucket}",
            "entry",
            "grace_sweeper",
            lookbackHours,
            takePerBucket);

        var bucketsScanned = 0;
        var candidates = 0;
        var recomputed = 0;

        foreach (var pk in HourBucketPks(nowUtc, lookbackHours))
        {
            ct.ThrowIfCancellationRequested();
            bucketsScanned++;

            IReadOnlyList<GraceIndexItem> items;
            var readWatch = Stopwatch.StartNew();

            try
            {
                // Fetch candidates whose graceEndsAtUtc <= now (store filters using RK/ticks).
                items = await _graceIndexStore.QueryExpiredAsync(pk, nowUtc, takePerBucket, ct);

                _logger.LogDebug(
                    "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} BucketPk={BucketPk} DurationMs={DurationMs} Count={Count}",
                    "persistence",
                    "grace_index.query_expired",
                    "GraceIndex",
                    pk,
                    readWatch.ElapsedMilliseconds,
                    items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Persistence read failed. LogCategory={LogCategory} Outcome={Outcome} BucketPk={BucketPk} DurationMs={DurationMs}",
                    "exception",
                    "dependency_failed",
                    pk,
                    readWatch.ElapsedMilliseconds);

                // If one bucket query fails, continue to next bucket.
                continue;
            }

            if (items.Count == 0)
                continue;

            candidates += items.Count;

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                using var userScope = _logger.BeginScope(new Dictionary<string, object?>
                {
                    ["OperationName"] = "grace_sweeper_user",
                    ["UserId"] = item.UserId,
                    ["CompanyId"] = null,
                    ["EntitlementId"] = null
                });

                try
                {
                    // Recompute access; orchestrator will persist snapshot and sync ManyChat best-effort.
                    var decision = await _accessOrchestrator.RecomputeForUserAsync(item.UserPk, item.UserId, persistUser: true, ct);
                    recomputed++;

                    _logger.LogDebug(
                        "Step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} Mode={Mode} Source={Source} GraceEndsAtUtc={GraceEndsAtUtc}",
                        "step",
                        "access_recompute",
                        "applied",
                        decision.Mode,
                        decision.Source,
                        decision.GraceEndsAtUtc);

                    // If user is no longer in individual grace, remove from grace index (cleanup).
                    // (Even if they remain in company grace, this index is only for individual grace.)
                    if (decision.Mode != Domain.Access.AccessMode.Grace
                        || (decision.Source & Domain.Access.AccessSource.Individual) == 0)
                    {
                        await _graceIndexStore.DeleteForUserAsync(new UserRef(item.UserPk, item.UserId), ct);

                        _logger.LogDebug(
                            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} Outcome={Outcome}",
                            "persistence",
                            "grace_index.delete_for_user",
                            "GraceIndex",
                            "applied");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "User recompute failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} UserPk={UserPk} UserId={UserId}",
                        "exception",
                        "dependency_failed",
                        "grace_sweep_user_failed",
                        item.UserPk,
                        item.UserId);

                    // Do not stop the sweep for one failing user.
                    continue;
                }
            }
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} BucketsScanned={BucketsScanned} Candidates={Candidates} Recomputed={Recomputed} DurationMs={DurationMs}",
            "outcome",
            "completed",
            bucketsScanned,
            candidates,
            recomputed,
            opWatch.ElapsedMilliseconds);
    }

    private static IEnumerable<string> HourBucketPks(DateTimeOffset nowUtc, int lookbackHours)
    {
        // include current hour and past hours
        for (var i = 0; i <= lookbackHours; i++)
        {
            var hour = nowUtc.AddHours(-i);
            yield return $"{TablePrefixes.Grace}_{hour:yyyyMMddHH}";
        }
    }
}

