using System.Diagnostics;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Timers;

public sealed class ProcessManyChatDispatchQueueTimerFunction
{
    private readonly ManyChatDispatchQueueProcessorService _processor;
    private readonly ILogger<ProcessManyChatDispatchQueueTimerFunction> _logger;

    public ProcessManyChatDispatchQueueTimerFunction(
        ManyChatDispatchQueueProcessorService processor,
        ILogger<ProcessManyChatDispatchQueueTimerFunction> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    [Function("ProcessManyChatDispatchQueueTimer")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timer, FunctionContext ctx)
    {
        var correlationId = LogContext.ResolveCorrelationId(null, ctx.InvocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_process_manychat_dispatch_queue",
            correlationId: correlationId,
            invocationId: ctx.InvocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_process_manychat_dispatch_queue",
            timer.IsPastDue);

        try
        {
            await _processor.RunBatchAsync(ct: ctx.CancellationToken);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.Completed,
                "manychat_dispatch_queue_processed",
                opWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "manychat_dispatch_queue_processing_failed",
                opWatch.ElapsedMilliseconds);
            throw;
        }
    }
}
