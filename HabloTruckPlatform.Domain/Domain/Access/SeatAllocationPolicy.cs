using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Access;

public static class SeatAllocationPolicy
{
    public static bool CanAssignSeat(Entitlement entitlement)
        => entitlement.Status == "active"
           && entitlement.SeatsUsed < entitlement.SeatsTotal;

    public static void AssignSeat(Entitlement entitlement)
    {
        if (!CanAssignSeat(entitlement))
            throw new InvalidOperationException("No seats available.");

        entitlement.SeatsUsed++;
    }

    public static void RevokeSeat(Entitlement entitlement)
    {
        if (entitlement.SeatsUsed > 0)
            entitlement.SeatsUsed--;
    }
}