using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IManyChatDispatchQueue
{
    Task EnqueueAsync(ManyChatDispatchMessage message, CancellationToken ct = default);

    Task<IReadOnlyList<ManyChatDispatchLease>> DequeueAsync(
        int maxMessages,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default);

    Task CompleteAsync(ManyChatDispatchLease lease, CancellationToken ct = default);
}
