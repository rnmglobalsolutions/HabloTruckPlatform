using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class StripeReconciliationTimerFunction
{
    private readonly StripeReconciliationService _service;
    private readonly ILogger<StripeReconciliationTimerFunction> _logger;

    public StripeReconciliationTimerFunction(
        StripeReconciliationService service,
        ILogger<StripeReconciliationTimerFunction> logger)
    {
        _service = service;
        _logger = logger;
    }

    [Function("StripeReconciliationTimer")]
    public async Task Run(
        [TimerTrigger("0 0 5 * * *")] TimerInfo timerInfo,
        FunctionContext context)
    {
        var correlationId = LogContext.ResolveCorrelationId(null, context.InvocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_stripe_reconciliation",
            correlationId: correlationId,
            invocationId: context.InvocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_stripe_reconciliation",
            timerInfo.IsPastDue);

        try
        {
            await _service.RunAsync(500, context.CancellationToken);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.Completed,
                "stripe_reconciliation_finished",
                opWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "stripe_reconciliation_failed",
                opWatch.ElapsedMilliseconds);
            throw;
        }
    }
}
