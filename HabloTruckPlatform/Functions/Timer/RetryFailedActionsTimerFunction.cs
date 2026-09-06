using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Functions.Timers;

public sealed class RetryFailedActionsTimerFunction
{
    private readonly FailedActionRetryService _retry;
    private readonly ILogger<RetryFailedActionsTimerFunction> _logger;

    public RetryFailedActionsTimerFunction(FailedActionRetryService retry, ILogger<RetryFailedActionsTimerFunction> logger)
    {
        _retry = retry;
        _logger = logger;
    }

    // Every 5 minutes (UTC)
    [Function("RetryFailedActionsTimer")]
    public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, FunctionContext ctx)
    {
        var correlationId = LogContext.ResolveCorrelationId(null, ctx.InvocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_retry_failed_actions",
            correlationId: correlationId,
            invocationId: ctx.InvocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_retry_failed_actions",
            timer.IsPastDue);

        try
        {
            await _retry.RetryDueAsync(lookbackHours: 12, take: 200, ct: ctx.CancellationToken);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.Completed,
                "retry_failed_actions_finished",
                opWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "retry_failed_actions_failed",
                opWatch.ElapsedMilliseconds);
            throw;
        }
    }
}
