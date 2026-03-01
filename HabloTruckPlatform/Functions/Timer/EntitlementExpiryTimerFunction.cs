using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
        var ct = ctx.CancellationToken;

        try
        {
            await _svc.RunAsync(take: 500, lookbackDays: 1, ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EntitlementExpiryTimer failed");
        }
    }
}