using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Models;

public enum SeatReservationOutcome
{
    Reserved,
    NotFound,
    Inactive,
    NoCapacity,
    OverCapacity,
    Conflict
}

public sealed record SeatReservationResult(
    SeatReservationOutcome Outcome,
    Entitlement? Entitlement,
    string? Reason = null);
