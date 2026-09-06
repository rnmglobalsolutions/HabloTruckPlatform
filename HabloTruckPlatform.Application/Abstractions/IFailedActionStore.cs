namespace HabloTruckPlatform.Application.Abstractions;

public interface IFailedActionStore
{
    Task EnsureTableAsync(CancellationToken ct = default);

    Task EnqueueAsync(
        string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default);

    Task<IReadOnlyList<FailedActionItem>> GetDueAsync(
        DateTimeOffset nowUtc, int lookbackHours, int take, CancellationToken ct = default);

    Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default);

    Task RescheduleAsync(
        string pk, string rk, int attempts, DateTimeOffset nextRetryUtc, string lastError,
        CancellationToken ct = default);

    Task MarkDeadAsync(
        string pk, string rk, int attempts, string lastError, CancellationToken ct = default);

    Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(
        DateTimeOffset nowUtc,
        string status,
        int lookbackHours,
        int take,
        CancellationToken ct = default);

    Task RequeueAsync(string pk, string rk, DateTimeOffset nextRetryUtc, CancellationToken ct = default);
}

public sealed record FailedActionItem(
    string Pk,
    string Rk,
    string ActionType,
    string PayloadJson,
    int Attempts,
    DateTimeOffset NextRetryUtc
);