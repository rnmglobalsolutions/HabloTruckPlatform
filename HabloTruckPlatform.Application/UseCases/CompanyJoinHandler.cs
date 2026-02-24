using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class CompanyJoinHandler
{
    private readonly IUserStore _userStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly ISeatAssignmentStore _seatStore;
    private readonly AccessOrchestrator _accessOrchestrator;

    public CompanyJoinHandler(
        IUserStore userStore,
        IEntitlementStore entitlementStore,
        ISeatAssignmentStore seatStore,
        AccessOrchestrator accessOrchestrator)
    {
        _userStore = userStore;
        _entitlementStore = entitlementStore;
        _seatStore = seatStore;
        _accessOrchestrator = accessOrchestrator;
    }

    public async Task HandleJoinAsync(JoinCompanyRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.UserPk) || string.IsNullOrWhiteSpace(req.UserId))
            throw new ArgumentException("UserPk and UserId are required.");

        if (string.IsNullOrWhiteSpace(req.CompanyId))
            throw new ArgumentException("CompanyId is required.");

        if (string.IsNullOrWhiteSpace(req.EntitlementId))
            throw new ArgumentException("EntitlementId is required.");

        var user = await _userStore.GetAsync(req.UserPk, req.UserId, ct)
                   ?? throw new InvalidOperationException("User not found.");

        // Load entitlement
        var ent = await _entitlementStore.GetAsync(req.CompanyId, req.EntitlementId, ct)
                  ?? throw new InvalidOperationException("Entitlement not found.");

        // Enforcement suave: solo verificamos que el entitlement esté "usable"
        // (si está expired/refunded/disabled, no tiene sentido asignar).
        if (!string.Equals(ent.Status, "active", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Entitlement status is '{ent.Status}', cannot join.");

        // Create/Upsert seat assignment (idempotent)
        var seat = new SeatAssignment
        {
            CompanyId = req.CompanyId,
            UserId = user.UserId,
            EntitlementId = ent.EntitlementId,
            Status = "active",
            AssignedAtUtc = DateTimeOffset.UtcNow
        };

        await _seatStore.UpsertAsync(seat, ct);

        // Update user company facts
        user.CompanyId = req.CompanyId;
        user.SeatEntitlementId = ent.EntitlementId;
        user.SeatStatus = "active";

        // Compute access snapshot (do NOT persist inside orchestrator)
        await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        // Single upsert user
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);
    }
}