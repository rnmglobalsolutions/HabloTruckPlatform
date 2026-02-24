namespace HabloTruckPlatform.Domain.Access;

public sealed record CompanyGracePolicy(int GraceDays)
{
    public TimeSpan Duration => TimeSpan.FromDays(GraceDays);

    public DateTimeOffset ComputeGraceEnd(DateTimeOffset entitlementEndUtc)
        => entitlementEndUtc.Add(Duration);

    public bool IsInGrace(DateTimeOffset nowUtc, DateTimeOffset entitlementEndUtc)
        => nowUtc < ComputeGraceEnd(entitlementEndUtc);
}