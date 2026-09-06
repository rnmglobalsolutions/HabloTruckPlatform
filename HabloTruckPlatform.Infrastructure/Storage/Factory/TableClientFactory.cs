using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace HabloTruckPlatform.Infrastructure.Storage.Factory;

public interface ITableClientFactory
{
    TableClient GetClient(string tableName);
    Task EnsureTableAsync(string tableName, CancellationToken ct = default);
}

public sealed class TableClientFactory : ITableClientFactory
{
    private readonly TableServiceClient _serviceClient;
    private readonly ConcurrentDictionary<string, TableClient> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<TableClientFactory> _logger;

    public TableClientFactory(TableServiceClient serviceClient, ILogger<TableClientFactory> logger)
    {
        _serviceClient = serviceClient;
        _logger = logger;
    }

    public TableClient GetClient(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name is required.", nameof(tableName));

        return _cache.GetOrAdd(tableName, n => _serviceClient.GetTableClient(n));
    }

    public async Task EnsureTableAsync(string tableName, CancellationToken ct = default)
    {
        var client = GetClient(tableName);
        try
        {
            await client.CreateIfNotExistsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure table exists: {Table}", tableName);
            throw;
        }
    }
}