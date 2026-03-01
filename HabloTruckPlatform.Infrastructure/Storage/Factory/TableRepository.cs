using Azure;
using Azure.Data.Tables;

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
    public async Task<T?> GetOrNullAsync<T>(TableClient table, string pk, string rk, CancellationToken ct = default)
        where T : class, ITableEntity
    {
        try
        {
            var resp = await table.GetEntityAsync<T>(pk, rk, cancellationToken: ct).ConfigureAwait(false);
            return resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task UpsertAsync<T>(TableClient table, T entity, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken ct = default)
        where T : class, ITableEntity
        => table.UpsertEntityAsync(entity, mode, ct);

    public async Task<bool> DeleteIfExistsAsync(TableClient table, string pk, string rk, CancellationToken ct = default)
    {
        try
        {
            await table.DeleteEntityAsync(pk, rk, ETag.All, ct).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<bool> TryInsertAsync<T>(TableClient table, T entity, CancellationToken ct = default)
        where T : class, ITableEntity
    {
        try
        {
            await table.AddEntityAsync(entity, ct).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // conflict => already exists
            return false;
        }
    }
}