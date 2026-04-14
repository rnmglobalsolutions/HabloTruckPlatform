using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class ManyChatDispatchQueueProcessorService
{
    private const int DefaultBatchSize = 32;

    private readonly IManyChatDispatchQueue _queue;
    private readonly IFailedActionStore _failedActionStore;
    private readonly FailedActionRetryService _failedActionRetryService;
    private readonly IClock _clock;
    private readonly IAppMetrics? _metrics;
    private readonly ILogger<ManyChatDispatchQueueProcessorService> _logger;

    public ManyChatDispatchQueueProcessorService(
        IManyChatDispatchQueue queue,
        IFailedActionStore failedActionStore,
        FailedActionRetryService failedActionRetryService,
        IClock clock,
        ILogger<ManyChatDispatchQueueProcessorService>? logger = null,
        IAppMetrics? metrics = null)
    {
        _queue = queue;
        _failedActionStore = failedActionStore;
        _failedActionRetryService = failedActionRetryService;
        _clock = clock;
        _metrics = metrics;
        _logger = logger ?? NullLogger<ManyChatDispatchQueueProcessorService>.Instance;
    }

    public async Task RunBatchAsync(
        int maxMessages = DefaultBatchSize,
        TimeSpan? visibilityTimeout = null,
        CancellationToken ct = default)
    {
        if (maxMessages <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxMessages));

        var leases = await _queue.DequeueAsync(
            maxMessages,
            visibilityTimeout ?? TimeSpan.FromMinutes(2),
            ct);

        foreach (var lease in leases)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _logger.LogInformation("#ManyChatDispatchQueueProcessorService - Entering Manychat Sync");
                await _failedActionRetryService.DispatchAsync(
                    lease.Message.ActionType,
                    lease.Message.PayloadJson,
                    ct);

                await _queue.CompleteAsync(lease, ct);
                _metrics?.ManyChatDispatchProcessed(lease.Message.ActionType, "completed");

                _logger.LogInformation(
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} DequeueCount={DequeueCount}",
                    "outcome",
                    "completed",
                    "manychat_dispatch_processed",
                    lease.Message.ActionType,
                    lease.DequeueCount);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogError("#ManyChatDispatchQueueProcessorService - Error ManayChat Synching - Operation Canceled");
                throw;
            }
            catch (ManyChatRequestException ex) when (ex.IsRetryable)
            {
                _logger.LogError("#ManyChatDispatchQueueProcessorService - Error Manychat Sync. Trying now Plan B: Moving to Table: Failed Actions to Sync to ManyChat.");
                await MoveToFailedActionsAsync(lease, "retryable_manychat_failure", ct);
                await _queue.CompleteAsync(lease, ct);
                _metrics?.ManyChatDispatchProcessed(lease.Message.ActionType, "moved_to_failed_actions");

                _logger.LogWarning(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} DequeueCount={DequeueCount} IsRetryable={IsRetryable}",
                    "exception",
                    "dependency_failed",
                    "manychat_dispatch_moved_to_failed_actions",
                    lease.Message.ActionType,
                    lease.DequeueCount,
                    ex.IsRetryable);
            }
            catch (ManyChatRequestException ex)
            {
                _logger.LogError("#ManyChatDispatchQueueProcessorService - Error in Manychat. No Sync");
                await _queue.CompleteAsync(lease, ct);
                _metrics?.ManyChatDispatchProcessed(lease.Message.ActionType, "dropped_non_retryable");

                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} DequeueCount={DequeueCount} IsRetryable={IsRetryable}",
                    "exception",
                    "validation_failed",
                    "manychat_dispatch_dropped_non_retryable",
                    lease.Message.ActionType,
                    lease.DequeueCount,
                    ex.IsRetryable);
            }
            catch (Exception ex)
            {
                await MoveToFailedActionsAsync(lease, "manychat_dispatch_unexpected_failure", ct);
                await _queue.CompleteAsync(lease, ct);
                _metrics?.ManyChatDispatchProcessed(lease.Message.ActionType, "moved_to_failed_actions");

                _logger.LogError(
                    ex,
                    "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} DequeueCount={DequeueCount}",
                    "exception",
                    "dependency_failed",
                    "manychat_dispatch_moved_to_failed_actions",
                    lease.Message.ActionType,
                    lease.DequeueCount);
            }
        }
    }

    private Task MoveToFailedActionsAsync(
        ManyChatDispatchLease lease,
        string reason,
        CancellationToken ct)
        => _failedActionStore.EnqueueAsync(
            lease.Message.ActionType,
            lease.Message.PayloadJson,
            _clock.UtcNow.AddMinutes(2),
            ct);
}
