using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IFailedActionStore
{
    Task SaveAsync(FailedAction action, CancellationToken ct = default);

    /// <summary>
    /// Optional: used by an admin replay endpoint / timer.
    /// </summary>
    Task<IReadOnlyList<FailedAction>> DequeuePendingAsync(int take = 100, CancellationToken ct = default);

    Task MarkCompletedAsync(string actionId, CancellationToken ct = default);

    Task IncrementRetryAsync(string actionId, string lastError, CancellationToken ct = default);
}