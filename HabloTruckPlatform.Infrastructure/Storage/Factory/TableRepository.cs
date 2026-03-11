using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Storage.Factory;

public interface ITableRepository
{
    Task<T?> GetOrNullAsync<T>(TableClient table, string pk, string rk, CancellationToken ct = default) where T : class, ITableEntity;
    Task UpsertAsync<T>(TableClient table, T entity, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken ct = default) where T : class, ITableEntity;
    Task<bool> DeleteIfExistsAsync(TableClient table, string pk, string rk, CancellationToken ct = default);
    Task<bool> TryInsertAsync<T>(TableClient table, T entity, CancellationToken ct = default) where T : class, ITableEntity;
}

public sealed class TableRepository : ITableRepository
{
    private readonly ILogger<TableRepository> _logger;

    public TableRepository(ILogger<TableRepository>? logger = null)
    {
        _logger = logger ?? NullLogger<TableRepository>.Instance;
    }

    public async Task<T?> GetOrNullAsync<T>(TableClient table, string pk, string rk, CancellationToken ct = default)
        where T : class, ITableEntity
    {
        var watch = Stopwatch.StartNew();

        try
        {
            var resp = await table.GetEntityAsync<T>(pk, rk, cancellationToken: ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Found={Found}",
                "persistence",
                "table.get",
                table.Name,
                pk,
                rk,
                watch.ElapsedMilliseconds,
                true);

            return resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(
                "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Found={Found}",
                "persistence",
                "table.get",
                table.Name,
                pk,
                rk,
                watch.ElapsedMilliseconds,
                false);
            return null;
        }
    }

    public async Task UpsertAsync<T>(TableClient table, T entity, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken ct = default)
        where T : class, ITableEntity
    {
        var watch = Stopwatch.StartNew();
        await table.UpsertEntityAsync(entity, mode, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} Mode={Mode} DurationMs={DurationMs} Success={Success}",
            "persistence",
            "table.upsert",
            table.Name,
            entity.PartitionKey,
            entity.RowKey,
            mode,
            watch.ElapsedMilliseconds,
            true);
    }

    public async Task<bool> DeleteIfExistsAsync(TableClient table, string pk, string rk, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            await table.DeleteEntityAsync(pk, rk, ETag.All, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "table.delete",
                table.Name,
                pk,
                rk,
                watch.ElapsedMilliseconds,
                true);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "table.delete",
                table.Name,
                pk,
                rk,
                watch.ElapsedMilliseconds,
                false);

            return false;
        }
    }

    public async Task<bool> TryInsertAsync<T>(TableClient table, T entity, CancellationToken ct = default)
        where T : class, ITableEntity
    {
        var watch = Stopwatch.StartNew();

        try
        {
            await table.AddEntityAsync(entity, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "table.insert",
                table.Name,
                entity.PartitionKey,
                entity.RowKey,
                watch.ElapsedMilliseconds,
                true);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // conflict => already exists
            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} RowKey={RowKey} DurationMs={DurationMs} Success={Success}",
                "persistence",
                "table.insert",
                table.Name,
                entity.PartitionKey,
                entity.RowKey,
                watch.ElapsedMilliseconds,
                false);

            return false;
        }
    }
}
