using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class EntitlementRecountServiceTests
{
    [Fact]
    public async Task RecountSeatsAsync_Should_CorrectStaleSeatsUsedDownward_AndClearOverCapacity()
    {
        var entitlements = new InMemoryEntitlementStore();
        var seats = new InMemorySeatStore();
        var service = new EntitlementRecountService(
            entitlements,
            seats,
            NullLogger<EntitlementRecountService>.Instance);

        await entitlements.UpsertAsync(new Entitlement
        {
            CompanyId = "C_RECOUNT",
            EntitlementId = "E_RECOUNT",
            SeatsTotal = 2,
            SeatsUsed = 5,
            IsOverCapacity = true,
            Status = "active",
            StartUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 3, 9, 0, 0, 0, TimeSpan.Zero)
        });

        await service.RecountSeatsAsync("C_RECOUNT", "E_RECOUNT");

        var entitlement = await entitlements.GetAsync("C_RECOUNT", "E_RECOUNT");

        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement!.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _rows = new();

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, entitlementId), out var entitlement) ? entitlement : null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemorySeatStore : ISeatAssignmentStore
    {
        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult<SeatAssignment?>(null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
