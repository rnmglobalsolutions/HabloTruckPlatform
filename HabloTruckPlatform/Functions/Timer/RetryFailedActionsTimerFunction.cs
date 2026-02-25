using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
        _logger.LogInformation("RetryFailedActionsTimer fired. IsPastDue={IsPastDue}", timer.IsPastDue);

        await _retry.RetryDueAsync(lookbackHours: 12, take: 200, ct: ctx.CancellationToken);
    }
}