using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class CompanyJoinHandler
{
    private readonly IUserStore _userStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly ISeatAssignmentStore _seatStore;
    private readonly AccessOrchestrator _accessOrchestrator;
    private readonly ILogger<CompanyJoinHandler> _logger;

    public CompanyJoinHandler(
        IUserStore userStore,
        IEntitlementStore entitlementStore,
        ISeatAssignmentStore seatStore,
        AccessOrchestrator accessOrchestrator,
        ILogger<CompanyJoinHandler>? logger = null)
    {
        _userStore = userStore;
        _entitlementStore = entitlementStore;
        _seatStore = seatStore;
        _accessOrchestrator = accessOrchestrator;
        _logger = logger ?? NullLogger<CompanyJoinHandler>.Instance;
    }

    public async Task HandleJoinAsync(JoinCompanyRequest req, CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["OperationName"] = "company_join",
            ["UserId"] = req.UserId,
            ["CompanyId"] = req.CompanyId,
            ["EntitlementId"] = req.EntitlementId,
            ["InviteCode"] = req.InviteCode
        });

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName}",
            "entry",
            "company_join");

        if (string.IsNullOrWhiteSpace(req.UserPk) || string.IsNullOrWhiteSpace(req.UserId))
            throw new ArgumentException("UserPk and UserId are required.");

        if (string.IsNullOrWhiteSpace(req.CompanyId))
            throw new ArgumentException("CompanyId is required.");

        if (string.IsNullOrWhiteSpace(req.EntitlementId))
            throw new ArgumentException("EntitlementId is required.");

        var userReadWatch = Stopwatch.StartNew();
        var user = await _userStore.GetAsync(req.UserPk, req.UserId, ct)
                   ?? throw new InvalidOperationException("User not found.");

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs}",
            "persistence",
            "user.get",
            "Users",
            userReadWatch.ElapsedMilliseconds);

        var entitlementReadWatch = Stopwatch.StartNew();
        var ent = await _entitlementStore.GetAsync(req.CompanyId, req.EntitlementId, ct)
                  ?? throw new InvalidOperationException("Entitlement not found.");

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} EntitlementStatus={EntitlementStatus}",
            "persistence",
            "entitlement.get",
            "Entitlements",
            entitlementReadWatch.ElapsedMilliseconds,
            ent.Status);

        // Enforcement suave: only verify entitlement is usable.
        if (!string.Equals(ent.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} EntitlementStatus={EntitlementStatus}",
                "decision",
                "entitlement_join_check",
                "denied",
                "entitlement_not_active",
                ent.Status);

            throw new InvalidOperationException($"Entitlement status is '{ent.Status}', cannot join.");
        }

        // Create/Upsert seat assignment (idempotent).
        var seat = new SeatAssignment
        {
            CompanyId = req.CompanyId,
            UserId = user.UserId,
            EntitlementId = ent.EntitlementId,
            Status = "active",
            AssignedAtUtc = DateTimeOffset.UtcNow
        };

        var seatWriteWatch = Stopwatch.StartNew();
        await _seatStore.UpsertAsync(seat, ct);

        _logger.LogDebug(
            "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} SeatStatus={SeatStatus}",
            "persistence",
            "seat_assignment.upsert",
            "Seats",
            seatWriteWatch.ElapsedMilliseconds,
            seat.Status);

        // Update user company facts.
        user.CompanyId = req.CompanyId;
        user.SeatEntitlementId = ent.EntitlementId;
        user.SeatStatus = "active";

        // Compute access snapshot (do not persist inside orchestrator).
        var recomputeWatch = Stopwatch.StartNew();
        await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);

        _logger.LogDebug(
            "Step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} DurationMs={DurationMs}",
            "step",
            "access_recompute",
            "applied",
            recomputeWatch.ElapsedMilliseconds);

        // Single upsert user.
        var userWriteWatch = Stopwatch.StartNew();
        await _userStore.UpsertAsync(user, ct);
        await _userStore.UpsertLookupsAsync(user, ct);

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} DurationMs={DurationMs}",
            "outcome",
            "completed",
            "company_join_applied",
            opWatch.ElapsedMilliseconds);
    }
}



