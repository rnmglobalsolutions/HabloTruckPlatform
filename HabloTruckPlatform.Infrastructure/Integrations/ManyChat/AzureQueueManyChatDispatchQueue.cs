using System.Text.Json;
using Azure.Storage.Queues;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Infrastructure.Integrations.ManyChat;

public sealed class AzureQueueManyChatDispatchQueue : IManyChatDispatchQueue, IAsyncDisposable
{
    private const string QueueName = "manychat-dispatch";
    private const int AzureQueueReceiveMaxMessages = 32;

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly QueueClient _queueClient;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    public AzureQueueManyChatDispatchQueue(string connectionString)
    {
        _queueClient = new QueueClient(connectionString, QueueName);
    }

    public async Task EnqueueAsync(ManyChatDispatchMessage message, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var payload = JsonSerializer.Serialize(message, JsonOpts);
        await _queueClient.SendMessageAsync(payload, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ManyChatDispatchLease>> DequeueAsync(
        int maxMessages,
        TimeSpan visibilityTimeout,
        CancellationToken ct = default)
    {
        if (maxMessages <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));

        var requestedMessages = Math.Min(maxMessages, AzureQueueReceiveMaxMessages);

        await EnsureInitializedAsync(ct);

        var response = await _queueClient.ReceiveMessagesAsync(
            maxMessages: requestedMessages,
            visibilityTimeout: visibilityTimeout,
            cancellationToken: ct);

        var leases = new List<ManyChatDispatchLease>(response.Value.Length);
        foreach (var message in response.Value)
        {
            if (string.IsNullOrWhiteSpace(message.MessageText))
                continue;

            var parsed = JsonSerializer.Deserialize<ManyChatDispatchMessage>(message.MessageText, JsonOpts);
            if (parsed is null)
                continue;

            leases.Add(new ManyChatDispatchLease(
                message.MessageId,
                message.PopReceipt,
                message.DequeueCount,
                parsed));
        }

        return leases;
    }

    public async Task CompleteAsync(ManyChatDispatchLease lease, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _queueClient.DeleteMessageAsync(lease.MessageId, lease.PopReceipt, ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
                return;

            await _queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
