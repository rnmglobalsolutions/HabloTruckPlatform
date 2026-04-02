using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
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
    private readonly IClock _clock;
    private readonly ILogger<CompanyJoinHandler> _logger;

    public CompanyJoinHandler(
        IUserStore userStore,
        IEntitlementStore entitlementStore,
        ISeatAssignmentStore seatStore,
        AccessOrchestrator accessOrchestrator,
        IClock clock,
        ILogger<CompanyJoinHandler>? logger = null)
    {
        _userStore = userStore;
        _entitlementStore = entitlementStore;
        _seatStore = seatStore;
        _accessOrchestrator = accessOrchestrator;
        _clock = clock;
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
                   ?? throw new CompanyJoinException("user_not_found", "User not found.");

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs}",
            "persistence",
            "user.get",
            "Users",
            userReadWatch.ElapsedMilliseconds);

        var entitlementReadWatch = Stopwatch.StartNew();
        var ent = await _entitlementStore.GetAsync(req.CompanyId, req.EntitlementId, ct)
                  ?? throw new CompanyJoinException("entitlement_not_found", "Entitlement not found.");

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} EntitlementStatus={EntitlementStatus}",
            "persistence",
            "entitlement.get",
            "Entitlements",
            entitlementReadWatch.ElapsedMilliseconds,
            ent.Status);

        var existingSeatReadWatch = Stopwatch.StartNew();
        var existingSeat = await _seatStore.GetAsync(req.CompanyId, user.UserId, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} Found={Found}",
            "persistence",
            "seat_assignment.get",
            "Seats",
            existingSeatReadWatch.ElapsedMilliseconds,
            existingSeat is not null);

        var existingSeatAlreadyActive = existingSeat is not null
            && existingSeat.IsActive()
            && string.Equals(existingSeat.EntitlementId, ent.EntitlementId, StringComparison.OrdinalIgnoreCase);

        if (existingSeatAlreadyActive)
        {
            if (ent.SeatsUsed < 1)
            {
                ent.SeatsUsed = 1;
                ent.IsOverCapacity = ent.SeatsUsed > ent.SeatsTotal;
                await _entitlementStore.UpsertAsync(ent, ct);
            }

            user.CompanyId = req.CompanyId;
            user.SeatEntitlementId = ent.EntitlementId;
            user.SeatStatus = "active";

            await _accessOrchestrator.RecomputeForUserAsync(user, persistUser: false, ct);
            await _userStore.UpsertAsync(user, ct);
            await _userStore.UpsertLookupsAsync(user, ct);
            return;
        }

        var activeSeatsFloor = await _seatStore.CountActiveSeatsAsync(req.CompanyId, ent.EntitlementId, ct);
        ent = await _entitlementStore.SyncSeatsUsedAsync(req.CompanyId, ent.EntitlementId, activeSeatsFloor, ct) ?? ent;
        ent.IsOverCapacity = ent.SeatsUsed > ent.SeatsTotal;

        if (!ent.IsActive(_clock.UtcNow))
        {
            _logger.LogInformation(
                "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason} EntitlementStatus={EntitlementStatus}",
                "decision",
                "entitlement_join_check",
                "denied",
                "entitlement_not_active",
                ent.Status);

            throw new CompanyJoinException("entitlement_not_active", $"Entitlement status is '{ent.Status}', cannot join.");
        }

        if (ent.IsOverCapacity)
            throw new CompanyJoinException("company_over_capacity", "Company is over capacity. No new seats can be assigned.");

        if (!existingSeatAlreadyActive)
        {
            var reservation = await _entitlementStore.TryReserveSeatAsync(req.CompanyId, ent.EntitlementId, ct);
            ent = reservation.Entitlement ?? ent;

            switch (reservation.Outcome)
            {
                case SeatReservationOutcome.NotFound:
                    throw new CompanyJoinException("entitlement_not_found", "Entitlement not found.");

                case SeatReservationOutcome.Inactive:
                    throw new CompanyJoinException("entitlement_not_active", $"Entitlement status is '{ent.Status}', cannot join.");

                case SeatReservationOutcome.OverCapacity:
                    throw new CompanyJoinException("company_over_capacity", "Company is over capacity. No new seats can be assigned.");

                case SeatReservationOutcome.NoCapacity:
                    throw new CompanyJoinException("no_seats_available", "No seats available for this company entitlement.");
            }

            var seat = new SeatAssignment
            {
                CompanyId = req.CompanyId,
                UserId = user.UserId,
                EntitlementId = ent.EntitlementId,
                Status = "active",
                AssignedAtUtc = _clock.UtcNow
            };

            var seatWriteWatch = Stopwatch.StartNew();
            var activation = await _seatStore.EnsureActiveAsync(seat, ct);

            _logger.LogDebug(
                "Persistence write completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} ActivationOutcome={ActivationOutcome}",
                "persistence",
                "seat_assignment.ensure_active",
                "Seats",
                seatWriteWatch.ElapsedMilliseconds,
                activation.Outcome);

            if (activation.Outcome == SeatActivationOutcome.AlreadyActive)
            {
                await _entitlementStore.ReleaseSeatReservationAsync(req.CompanyId, ent.EntitlementId, ct);
            }
            else if (activation.Outcome != SeatActivationOutcome.Activated)
            {
                await _entitlementStore.ReleaseSeatReservationAsync(req.CompanyId, ent.EntitlementId, ct);
                throw new CompanyJoinException("seat_assignment_conflict", "Unable to assign company seat at this time.");
            }

            ent = await _entitlementStore.GetAsync(req.CompanyId, ent.EntitlementId, ct) ?? ent;
        }

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
