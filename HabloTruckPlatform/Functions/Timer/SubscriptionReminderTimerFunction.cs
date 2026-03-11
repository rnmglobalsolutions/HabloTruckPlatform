using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Functions.Timers;

public sealed class SubscriptionReminderTimerFunction
{
    private readonly SubscriptionReminderService _service;
    private readonly ILogger<SubscriptionReminderTimerFunction> _logger;

    public SubscriptionReminderTimerFunction(
        SubscriptionReminderService service,
        ILogger<SubscriptionReminderTimerFunction> logger)
    {
        _service = service;
        _logger = logger;
    }

    // Daily at 13:30 UTC
    [Function("SubscriptionReminderTimer")]
    public async Task Run([TimerTrigger("0 30 13 * * *")] TimerInfo timer, FunctionContext ctx)
    {
        var correlationId = LogContext.ResolveCorrelationId(null, ctx.InvocationId);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_subscription_reminders",
            correlationId: correlationId,
            invocationId: ctx.InvocationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_subscription_reminders",
            timer.IsPastDue);

        try
        {
            await _service.RunDailyAsync(take: 2000, ct: ctx.CancellationToken);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Outcome,
                LogContext.Outcomes.Completed,
                "subscription_reminder_timer_finished",
                opWatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Operation failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
                LogContext.Categories.Exception,
                LogContext.Outcomes.DependencyFailed,
                "subscription_reminder_timer_failed",
                opWatch.ElapsedMilliseconds);
            throw;
        }
    }
}
