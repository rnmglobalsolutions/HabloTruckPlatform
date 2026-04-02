namespace HabloTruckPlatform.Application.Models;

public sealed record ManyChatDispatchMessage(
    string ActionType,
    string PayloadJson,
    string? CorrelationId,
    DateTimeOffset EnqueuedAtUtc);

public sealed record ManyChatDispatchLease(
    string MessageId,
    string PopReceipt,
    long DequeueCount,
    ManyChatDispatchMessage Message);
