namespace HabloTruckPlatform.Domain.Abstractions;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}