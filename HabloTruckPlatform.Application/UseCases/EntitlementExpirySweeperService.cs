using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class EntitlementExpirySweeperService
{
    private readonly IEntitlementExpiryIndexStore _expiryIndex;
    private readonly IEntitlementStore _entitlements;
    private readonly EntitlementRecountService _recount;
    private readonly IClock _clock;
    private readonly ILogger<EntitlementExpirySweeperService> _logger;

    public EntitlementExpirySweeperService(
        IEntitlementExpiryIndexStore expiryIndex,
        IEntitlementStore entitlements,
        EntitlementRecountService recount,
        IClock clock,
        ILogger<EntitlementExpirySweeperService> logger)
    {
        _expiryIndex = expiryIndex;
        _entitlements = entitlements;
        _recount = recount;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(int take = 500, int lookbackDays = 1, CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();
        var nowUtc = _clock.UtcNow;

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} Take={Take} LookbackDays={LookbackDays}",
            "entry",
            "entitlement_expiry_sweeper",
            take,
            lookbackDays);

        // Deduplicate recount work: "companyId|entitlementId".
        var recountKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var partitionsScanned = 0;
        var rowsRead = 0;
        var expiredApplied = 0;
        var orphanDeleted = 0;

        for (var d = 0; d <= lookbackDays; d++)
        {
            ct.ThrowIfCancellationRequested();
            partitionsScanned++;

            var day = nowUtc.AddDays(-d);
            var pk = ExpiryPk(day);

            var queryWatch = Stopwatch.StartNew();
            var items = await _expiryIndex.QueryExpiringAsync(pk, nowUtc, take, ct);
            rowsRead += items.Count;

            _logger.LogDebug(
                "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} PartitionKey={PartitionKey} DurationMs={DurationMs} Count={Count}",
                "persistence",
                "entitlement_expiry_index.query_expiring",
                "EntitlementExpiryIndex",
                pk,
                queryWatch.ElapsedMilliseconds,
                items.Count);

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                using var rowScope = _logger.BeginScope(new Dictionary<string, object?>
                {
                    ["OperationName"] = "entitlement_expiry_row",
                    ["CompanyId"] = item.CompanyId,
                    ["EntitlementId"] = item.EntitlementId
                });

                try
                {
                    var entitlementReadWatch = Stopwatch.StartNew();
                    var ent = await _entitlements.GetAsync(item.CompanyId, item.EntitlementId, ct);

                    _logger.LogDebug(
                        "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
                        "persistence",
                        "entitlement.get",
                        "Entitlements",
                        entitlementReadWatch.ElapsedMilliseconds,
                        ent is not null);

                    if (ent is null)
                    {
                        await _expiryIndex.DeleteAsync(item.Pk, item.Rk, ct);
                        orphanDeleted++;

                        _logger.LogInformation(
                            "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason}",
                            "outcome",
                            "completed",
                            "orphan_expiry_index_deleted");
                        continue;
                    }

                    if (!ent.IsExpired(nowUtc))
                    {
                        _logger.LogInformation(
                            "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} Status={Status} EndUtc={EndUtc}",
                            "decision",
                            "entitlement_expiry_check",
                            "no_action_needed",
                            "entitlement_not_expired",
                            ent.Status,
                            ent.EndUtc);
                        continue;
                    }

                    // Mark expired if needed (idempotent).
                    var changed = !string.Equals(ent.Status, "expired", StringComparison.OrdinalIgnoreCase);
                    if (changed)
                    {
                        ent.Status = "expired";
                        ent.UpdatedAtUtc = nowUtc;
                        await _entitlements.UpsertAsync(ent, ct);

                        _logger.LogDebug(
                            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} Outcome={Outcome}",
                            "persistence",
                            "entitlement.upsert",
                            "Entitlements",
                            "expired");
                    }

                    // Delete processed index row so we do not re-process every run.
                    await _expiryIndex.DeleteAsync(item.Pk, item.Rk, ct);

                    // Immediate recount for reporting accuracy (SeatsUsed).
                    recountKeys.Add($"{item.CompanyId}|{item.EntitlementId}");
                    expiredApplied++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Row processing failed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason}",
                        "exception",
                        "dependency_failed",
                        "entitlement_expiry_row_failed");
                }
            }
        }

        // Best-effort recount.
        var recountSucceeded = 0;
        var recountFailed = 0;

        foreach (var key in recountKeys)
        {
            ct.ThrowIfCancellationRequested();

            var parts = key.Split('|', 2);
            var companyId = parts[0];
            var entitlementId = parts.Length > 1 ? parts[1] : "";

            if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
                continue;

            try
            {
                await _recount.RecountSeatsAsync(companyId, entitlementId, ct);
                recountSucceeded++;
            }
            catch (Exception ex)
            {
                recountFailed++;

                _logger.LogError(
                    ex,
                    "Recount failed. LogCategory={LogCategory} Outcome={Outcome} CompanyId={CompanyId} EntitlementId={EntitlementId}",
                    "exception",
                    "dependency_failed",
                    companyId,
                    entitlementId);
            }
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} PartitionsScanned={PartitionsScanned} RowsRead={RowsRead} ExpiredApplied={ExpiredApplied} OrphanDeleted={OrphanDeleted} RecountQueued={RecountQueued} RecountSucceeded={RecountSucceeded} RecountFailed={RecountFailed} DurationMs={DurationMs}",
            "outcome",
            "completed",
            partitionsScanned,
            rowsRead,
            expiredApplied,
            orphanDeleted,
            recountKeys.Count,
            recountSucceeded,
            recountFailed,
            opWatch.ElapsedMilliseconds);
    }

    private static string ExpiryPk(DateTimeOffset utc)
        => $"{TablePrefixes.EntitlementExpiry}_{utc:yyyyMMdd}";
}

