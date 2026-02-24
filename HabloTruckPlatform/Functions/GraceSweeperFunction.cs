using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions;

public class GraceSweeperFunction
{
    private readonly ILogger _logger;

    public GraceSweeperFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<GraceSweeperFunction>();
    }

    [Function("GraceSweeperFunction")]
    public void Run([TimerTrigger("0 0 * * * *")] TimerInfo myTimer) // every hour
    {
        _logger.LogInformation("C# Timer trigger function executed at: {executionTime}", DateTime.Now);
        
        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation("Next timer schedule at: {nextSchedule}", myTimer.ScheduleStatus.Next);
        }
    }
}