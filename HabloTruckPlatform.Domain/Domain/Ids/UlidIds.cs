namespace HabloTruckPlatform.Domain.Ids;

public static class UlidIds
{
    public static string NewUserId() => NewId();
    public static string NewCompanyId() => NewId();
    public static string NewEntitlementId() => NewId();
    public static string NewInviteCodeId() => NewId();
    public static string NewSeatAssignmentId() => NewId();
    public static string NewFailedActionId() => NewId();

    private static string NewId()
    {
        return Ulid.NewUlid().ToString();
    }
}