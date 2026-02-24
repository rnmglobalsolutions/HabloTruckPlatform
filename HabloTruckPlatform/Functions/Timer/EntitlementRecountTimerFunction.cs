using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.UseCases;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Timers;

public sealed class EntitlementRecountTimerFunction
{
    private readonly IEntitlementExpiryIndexStore _expiryIndex;
    private readonly EntitlementRecountService _recount;
    private readonly ILogger<EntitlementRecountTimerFunction> _logger;

    public EntitlementRecountTimerFunction(
        IEntitlementExpiryIndexStore expiryIndex,
        EntitlementRecountService recount,
        ILogger<EntitlementRecountTimerFunction> logger)
    {
        _expiryIndex = expiryIndex;
        _recount = recount;
        _logger = logger;
    }

    // Daily at 05:00 UTC
    [Function("EntitlementRecountTimer")]
    public async Task Run([TimerTrigger("0 0 5 * * *")] TimerInfo timer)
    {
        _logger.LogInformation("EntitlementRecountTimer fired. IsPastDue={IsPastDue}", timer.IsPastDue);

        var nowUtc = DateTimeOffset.UtcNow;

        // Today's expiry partition (yyyyMMdd)
        var pk = $"HT#EE#{nowUtc:yyyyMMdd}";

        // Pull up to N expiring items, recount them (cheap)
        var items = await _expiryIndex.QueryExpiringAsync(pk, nowUtc, take: 500);

        foreach (var it in items)
        {
            await _recount.RecountSeatsAsync(it.CompanyId, it.EntitlementId);
        }
    }
}