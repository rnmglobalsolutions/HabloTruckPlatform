using HabloTruckPlatform.Application.Abstractions;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Daily job: recalculates SeatsUsed for entitlements (soft enforcement/reporting).
/// You can call this from a Timer Function once per day.
/// </summary>
public sealed class EntitlementRecountService
{
    private readonly IEntitlementStore _entitlementStore;
    private readonly ISeatAssignmentStore _seatStore;

    public EntitlementRecountService(IEntitlementStore entitlementStore, ISeatAssignmentStore seatStore)
    {
        _entitlementStore = entitlementStore;
        _seatStore = seatStore;
    }

    public async Task RecountSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
    {
        var ent = await _entitlementStore.GetAsync(companyId, entitlementId, ct);
        if (ent is null) return;

        var used = await _seatStore.CountActiveSeatsAsync(companyId, entitlementId, ct);

        // Soft enforcement: just update SeatsUsed for accurate reporting
        ent.SeatsUsed = used;

        await _entitlementStore.UpsertAsync(ent, ct);
    }
}