using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
        _logger.LogInformation("GraceTimer fired. IsPastDue={IsPastDue}", timer.IsPastDue);

        // Look back 96h, process 500 per bucket
        await _sweeper.SweepExpiredGraceAsync(lookbackHours: 96, takePerBucket: 500);
    }
}