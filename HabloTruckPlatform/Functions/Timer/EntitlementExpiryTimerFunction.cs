using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Functions.Functions.Timer;

public sealed class EntitlementExpiryTimerFunction
{
    private readonly EntitlementExpirySweeperService _svc;
    private readonly ILogger<EntitlementExpiryTimerFunction> _logger;

    public EntitlementExpiryTimerFunction(
        EntitlementExpirySweeperService svc,
        ILogger<EntitlementExpiryTimerFunction> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    [Function("EntitlementExpiryTimer")]
    public async Task Run(
        [TimerTrigger("0 10 * * * *")] TimerInfo timer,
        FunctionContext ctx)
    {
        var correlationId = LogContext.ResolveCorrelationId(null, ctx.InvocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_entitlement_expiry",
            correlationId: correlationId,
            invocationId: ctx.InvocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_entitlement_expiry",
            timer.IsPastDue);

        try
        {
            await _svc.RunAsync(take: 500, lookbackDays: 1, ct: ctx.CancellationToken);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.Completed,
                "entitlement_expiry_timer_finished",
                opWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "entitlement_expiry_timer_failed",
                opWatch.ElapsedMilliseconds);
            throw;
        }
    }
}
