namespace HabloTruckPlatform.Domain.Access;

public sealed record GracePolicy(int GraceHours)
{
    public TimeSpan Duration => TimeSpan.FromHours(GraceHours);

    public DateTimeOffset ComputeGraceEnd(DateTimeOffset nowUtc)
        => nowUtc.Add(Duration);
}