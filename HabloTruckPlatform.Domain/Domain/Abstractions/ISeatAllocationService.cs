using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Domain.Abstractions;

public interface ISeatAllocationService
{
    bool CanAssignSeat(Entitlement entitlement);
    void AssignSeat(Entitlement entitlement);
    void RevokeSeat(Entitlement entitlement);
}