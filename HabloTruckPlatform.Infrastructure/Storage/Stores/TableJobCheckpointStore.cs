using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableJobCheckpointStore : IJobCheckpointStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableJobCheckpointStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient Table => _factory.GetClient(TableNames.JobCheckpoints);

    public async Task<string?> GetCursorAsync(string jobName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            throw new ArgumentException("Job name is required.", nameof(jobName));

        var entity = await _repo.GetOrNullAsync<JobCheckpointEntity>(Table, "job", jobName.Trim(), ct);
        return entity?.Cursor;
    }

    public Task UpsertCursorAsync(string jobName, string cursor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobName))
            throw new ArgumentException("Job name is required.", nameof(jobName));

        var entity = new JobCheckpointEntity
        {
            PartitionKey = "job",
            RowKey = jobName.Trim(),
            Cursor = cursor?.Trim() ?? string.Empty,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return _repo.UpsertAsync(Table, entity, TableUpdateMode.Replace, ct);
    }
}
