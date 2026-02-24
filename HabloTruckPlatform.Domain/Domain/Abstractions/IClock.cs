namespace HabloTruckPlatform.Domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}