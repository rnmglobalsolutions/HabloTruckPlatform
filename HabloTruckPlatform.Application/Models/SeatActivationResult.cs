namespace HabloTruckPlatform.Application.Models;

public enum SeatActivationOutcome
{
    Activated,
    AlreadyActive,
    Conflict
}

public sealed record SeatActivationResult(
    SeatActivationOutcome Outcome,
    string? Reason = null);
