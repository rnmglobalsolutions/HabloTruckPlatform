namespace HabloTruckPlatform.Application.Abstractions;

public interface ISubscriptionReminderStore
{
    /// <summary>
    /// Atomically records a sent reminder for (subscription + reminder type + period end).
    /// Returns false when already recorded (idempotency guard).
    /// </summary>
    Task<bool> TryMarkSentAsync(
        string subscriptionId,
        string reminderType,
        DateTimeOffset periodEndUtc,
        DateTimeOffset sentAtUtc,
        CancellationToken ct = default);
}
