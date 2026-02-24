// src/HabloTruck.Platform.Application/Abstractions/IStripeEventStore.cs
namespace HabloTruckPlatform.Application.Abstractions;

public interface IStripeEventStore
{
    /// <summary>
    /// Returns false if already processed (idempotency).
    /// Must be atomic (insert-if-not-exists).
    /// </summary>
    Task<bool> TryMarkProcessedAsync(
        string stripeEventId, string eventType, DateTimeOffset createdUtc, CancellationToken ct = default);
}