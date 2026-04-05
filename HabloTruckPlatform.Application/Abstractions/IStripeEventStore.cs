namespace HabloTruckPlatform.Application.Abstractions;

public enum StripeEventProcessingStartResult
{
    Started,
    AlreadyProcessed,
    AlreadyInProgress
}

public interface IStripeEventStore
{
    Task<StripeEventProcessingStartResult> TryStartProcessingAsync(
        string stripeEventId,
        string eventType,
        DateTimeOffset createdUtc,
        CancellationToken ct = default);

    Task MarkProcessedAsync(string stripeEventId, CancellationToken ct = default);

    Task ReleaseProcessingAsync(string stripeEventId, CancellationToken ct = default);
}
