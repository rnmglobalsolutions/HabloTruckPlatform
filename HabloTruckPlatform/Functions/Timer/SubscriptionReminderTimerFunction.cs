using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
        _logger.LogInformation("SubscriptionReminderTimer fired. IsPastDue={IsPastDue}", timer.IsPastDue);
        await _service.RunDailyAsync(take: 2000, ct: ctx.CancellationToken);
    }
}
