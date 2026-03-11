using HabloTruckPlatform.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Daily job: recalculates SeatsUsed for entitlements (soft enforcement/reporting).
/// You can call this from a Timer Function once per day.
/// </summary>
public sealed class EntitlementRecountService
{
    private readonly IEntitlementStore _entitlementStore;
    private readonly ISeatAssignmentStore _seatStore;
    private readonly ILogger<EntitlementRecountService> _logger;

    public EntitlementRecountService(
        IEntitlementStore entitlementStore,
        ISeatAssignmentStore seatStore,
        ILogger<EntitlementRecountService>? logger = null)
    {
        _entitlementStore = entitlementStore;
        _seatStore = seatStore;
        _logger = logger ?? NullLogger<EntitlementRecountService>.Instance;
    }

    public async Task RecountSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "entitlement_recount",
            ["CompanyId"] = companyId,
            ["EntitlementId"] = entitlementId
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            "entry",
            "entitlement_recount");

        var readWatch = Stopwatch.StartNew();
        var ent = await _entitlementStore.GetAsync(companyId, entitlementId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "entitlement.get",
            "Entitlements",
            readWatch.ElapsedMilliseconds,
            ent is not null);

        if (ent is null)
        {
            _logger.LogInformation(
                "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason}",
                "outcome",
                "no_action_needed",
                "entitlement_not_found");
            return;
        }

        var countWatch = Stopwatch.StartNew();
        var used = await _seatStore.CountActiveSeatsAsync(companyId, entitlementId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} SeatsUsed={SeatsUsed}",
            "persistence",
            "seat_assignment.count_active",
            "Seats",
            countWatch.ElapsedMilliseconds,
            used);

        // Soft enforcement: just update SeatsUsed for accurate reporting.
        ent.SeatsUsed = used;

        var writeWatch = Stopwatch.StartNew();
        await _entitlementStore.UpsertAsync(ent, ct);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} SeatsUsed={SeatsUsed} DurationMs={DurationMs}",
            "outcome",
            "completed",
            "entitlement_seat_count_updated",
            used,
            opWatch.ElapsedMilliseconds);
    }
}



