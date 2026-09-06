namespace HabloTruckPlatform.Application.Abstractions;

public interface IJobCheckpointStore
{
    Task<string?> GetCursorAsync(string jobName, CancellationToken ct = default);
    Task UpsertCursorAsync(string jobName, string cursor, CancellationToken ct = default);
}
