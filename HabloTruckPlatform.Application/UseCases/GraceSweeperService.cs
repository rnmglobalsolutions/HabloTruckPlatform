using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;

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

    public GraceSweeperService(
        IGraceIndexStore graceIndexStore,
        AccessOrchestrator accessOrchestrator,
        IClock clock)
    {
        _graceIndexStore = graceIndexStore;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
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

        var nowUtc = _clock.UtcNow;

        foreach (var pk in HourBucketPks(nowUtc, lookbackHours))
        {
            // Fetch candidates whose graceEndsAtUtc <= now (store filters using RK/ticks)
            IReadOnlyList<GraceIndexItem> items;

            try
            {
                items = await _graceIndexStore.QueryExpiredAsync(pk, nowUtc, takePerBucket, ct);
            }
            catch
            {
                // If one bucket query fails, continue to next bucket.
                continue;
            }

            if (items.Count == 0)
                continue;

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // Recompute access; orchestrator will persist snapshot and sync ManyChat best-effort
                    var decision = await _accessOrchestrator.RecomputeForUserAsync(item.UserPk, item.UserId, persistUser: true, ct);

                    // If user is no longer in individual grace, remove from grace index (cleanup)
                    // (Even if they remain in company grace, this index is only for individual grace.)
                    if (decision.Mode != Domain.Access.AccessMode.Grace
                        || (decision.Source & Domain.Access.AccessSource.Individual) == 0)
                    {
                        await _graceIndexStore.DeleteForUserAsync(new UserRef(item.UserPk, item.UserId), ct);
                    }
                }
                catch
                {
                    // Do not stop the sweep for one failing user.
                    // You can later add FailedAction outbox here if you want.
                    continue;
                }
            }
        }
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