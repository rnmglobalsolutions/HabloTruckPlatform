using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
        var correlationId = LogContext.ResolveCorrelationId(null);
        var opWatch = Stopwatch.StartNew();

        using var scope = LogContext.BeginOperationScope(
            _logger,
            operationName: "timer_entitlement_recount",
            correlationId: correlationId);

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} IsPastDue={IsPastDue}",
            LogContext.Categories.Entry,
            "timer_entitlement_recount",
            timer.IsPastDue);

        var nowUtc = DateTimeOffset.UtcNow;

        // Today's expiry partition (yyyyMMdd).
        var pk = $"{TablePrefixes.EntitlementExpiry}_{nowUtc:yyyyMMdd}";

        var queryWatch = Stopwatch.StartNew();
        var items = await _expiryIndex.QueryExpiringAsync(pk, nowUtc, take: 500);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} DurationMs={DurationMs} Count={Count}",
            LogContext.Categories.Persistence,
            "entitlement_expiry_index.query_expiring",
            "EntitlementExpiryIndex",
            pk,
            queryWatch.ElapsedMilliseconds,
            items.Count);

        var recounted = 0;

        foreach (var it in items)
        {
            await _recount.RecountSeatsAsync(it.CompanyId, it.EntitlementId);
            recounted++;
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} Recounted={Recounted} DurationMs={DurationMs}",
            LogContext.Categories.Outcome,
            LogContext.Outcomes.Completed,
            "entitlement_recount_timer_finished",
            recounted,
            opWatch.ElapsedMilliseconds);
    }
}

