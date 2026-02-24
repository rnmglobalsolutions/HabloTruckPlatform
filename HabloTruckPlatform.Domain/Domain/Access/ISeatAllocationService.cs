using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Access;

public interface ISeatAllocationService
{
    bool CanAssignSeat(Entitlement entitlement);
    void AssignSeat(Entitlement entitlement);
    void RevokeSeat(Entitlement entitlement);
}