using HabloTruckPlatform.Domain.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Access;

public class SeatAllocationPolicy : ISeatAllocationService
{
    public bool CanAssignSeat(Entitlement entitlement)
        => entitlement.Status == "active"
           && entitlement.SeatsUsed < entitlement.SeatsTotal;

    public void AssignSeat(Entitlement entitlement)
    {
        if (!CanAssignSeat(entitlement))
            throw new InvalidOperationException("No seats available.");

        entitlement.SeatsUsed++;
    }

    public void RevokeSeat(Entitlement entitlement)
    {
        if (entitlement.SeatsUsed > 0)
            entitlement.SeatsUsed--;
    }
}