namespace HabloTruckPlatform.Application.Models;

public sealed record FailedAction
{
    public required string ActionId { get; init; }          // ULID/GUID
    public required string ActionType { get; init; }        // e.g., "manychat_sync_access"
    public required string CorrelationId { get; init; }     // stripeEventId, userId, etc.
    public required string PayloadJson { get; init; }
    public required string Status { get; init; }            // "pending" | "completed"
    public required int RetryCount { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastTriedAtUtc { get; init; }
    public string? LastError { get; init; }
}