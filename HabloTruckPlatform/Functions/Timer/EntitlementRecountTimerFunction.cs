using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Infrastructure.Telemetry;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Functions.Timers;

public sealed class EntitlementRecountTimerFunction
{
    private readonly IEntitlementStore _entitlements;
    private readonly EntitlementRecountService _recount;
    private readonly ILogger<EntitlementRecountTimerFunction> _logger;

    public EntitlementRecountTimerFunction(
        IEntitlementStore entitlements,
        EntitlementRecountService recount,
        ILogger<EntitlementRecountTimerFunction> logger)
    {
        _entitlements = entitlements;
        _recount = recount;
        _logger = logger;
    }

    // Hourly at minute 0 UTC
    [Function("EntitlementRecountTimer")]
    public async Task Run([TimerTrigger("0 0 * * * *")] TimerInfo timer)
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

        var queryWatch = Stopwatch.StartNew();
        var items = await _entitlements.QueryForRecountAsync(ct: default);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Count={Count}",
            LogContext.Categories.Persistence,
            "entitlement.query_for_recount",
            "Entitlements",
            queryWatch.ElapsedMilliseconds,
            items.Count);

        var recounted = 0;

        foreach (var it in items)
        {
            if (string.Equals(it.Status, "expired", StringComparison.OrdinalIgnoreCase)
                || string.Equals(it.Status, "refunded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(it.Status, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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
