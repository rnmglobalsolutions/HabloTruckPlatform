using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Access;

public sealed record UserAccessContext(
    User User,
    SeatAssignment? Seat,
    Entitlement? Entitlement);