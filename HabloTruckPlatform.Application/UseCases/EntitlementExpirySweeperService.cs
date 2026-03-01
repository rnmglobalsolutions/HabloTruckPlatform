using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Abstractions;
using Microsoft.Extensions.Logging;

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
        var nowUtc = _clock.UtcNow;

        // Deduplicate recount work: "companyId|entitlementId"
        var recountKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var d = 0; d <= lookbackDays; d++)
        {
            ct.ThrowIfCancellationRequested();

            var day = nowUtc.AddDays(-d);
            var pk = ExpiryPk(day);

            var items = await _expiryIndex.QueryExpiringAsync(pk, nowUtc, take, ct);
            if (items.Count == 0) continue;

            _logger.LogInformation("EntitlementExpirySweeper: pk={Pk}, items={Count}", pk, items.Count);

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var ent = await _entitlements.GetAsync(item.CompanyId, item.EntitlementId, ct);

                    if (ent is null)
                    {
                        // Orphan index row -> delete it
                        await _expiryIndex.DeleteAsync(item.Pk, item.Rk, ct);
                        continue;
                    }

                    // ✅ Use domain rule
                    if (!ent.IsExpired(nowUtc))
                        continue;

                    // Mark expired if needed (idempotent)
                    var changed = !string.Equals(ent.Status, "expired", StringComparison.OrdinalIgnoreCase);
                    if (changed)
                    {
                        ent.Status = "expired";
                        ent.UpdatedAtUtc = nowUtc;
                        await _entitlements.UpsertAsync(ent, ct);
                    }

                    // ✅ Delete processed index row so we don't re-process every run
                    await _expiryIndex.DeleteAsync(item.Pk, item.Rk, ct);

                    // ✅ Immediate recount for reporting accuracy (SeatsUsed)
                    recountKeys.Add($"{item.CompanyId}|{item.EntitlementId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "EntitlementExpirySweeper failed for company={CompanyId} entitlement={EntitlementId}",
                        item.CompanyId, item.EntitlementId);
                }
            }
        }

        // Best-effort recount
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "EntitlementExpirySweeper recount failed for company={CompanyId} entitlement={EntitlementId}",
                    companyId, entitlementId);
            }
        }
    }

    private static string ExpiryPk(DateTimeOffset utc)
        => $"HT#EE#{utc:yyyyMMdd}";
}