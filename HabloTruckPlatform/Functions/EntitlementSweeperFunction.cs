using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions;

public class EntitlementSweeperFunction
{
    private readonly ILogger _logger;

    public EntitlementSweeperFunction(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<EntitlementSweeperFunction>();
    }

    [Function("EntitlementSweeperFunction")]
    public void Run([TimerTrigger("0 0 3 * * *")] TimerInfo myTimer) // (daily at 3 AM UTC)
    {
        _logger.LogInformation("C# Timer trigger function executed at: {executionTime}", DateTime.Now);
        
        if (myTimer.ScheduleStatus is not null)
        {
            _logger.LogInformation("Next timer schedule at: {nextSchedule}", myTimer.ScheduleStatus.Next);
        }
    }
}