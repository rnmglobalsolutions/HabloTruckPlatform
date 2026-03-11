using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Functions.Timers;

public sealed class GraceTimerFunction
{
    private readonly GraceSweeperService _sweeper;
    private readonly ILogger<GraceTimerFunction> _logger;

    public GraceTimerFunction(GraceSweeperService sweeper, ILogger<GraceTimerFunction> logger)
    {
        _sweeper = sweeper;
        _logger = logger;
    }

    // Every hour at minute 0 (UTC)
    [Function("GraceTimer")]
    public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer)
    {
        var correlationId = LogContext.ResolveCorrelationId(null);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_grace_sweeper",
            correlationId: correlationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_grace_sweeper",
            timer.IsPastDue);

        try
        {
            // Look back 96h, process 500 per bucket.
            await _sweeper.SweepExpiredGraceAsync(lookbackHours: 96, takePerBucket: 500);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.Completed,
                "grace_sweep_finished",
                opWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "grace_sweep_failed",
                opWatch.ElapsedMilliseconds);
            throw;
        }
    }
}
