using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IInviteCodeStore
{
    Task EnsureTableAsync(CancellationToken ct = default);

    Task<InviteCode?> GetAsync(string code, CancellationToken ct = default);

    Task CreateAsync(InviteCode invite, CancellationToken ct = default);

    /// <summary>
    /// Attempts to claim one use of the invite (Uses++) with optimistic concurrency.
    /// Returns false if exhausted/expired/disabled or if contention prevents update after retry.
    /// </summary>
    Task<bool> TryConsumeAsync(string code, DateTimeOffset nowUtc, CancellationToken ct = default);

    Task ReleaseConsumptionAsync(string code, CancellationToken ct = default)
        => Task.CompletedTask;

    Task UpsertAsync(InviteCode invite, CancellationToken ct = default);

    Task<IReadOnlyList<InviteCode>> ListForCompanyAsync(
        string companyId, int take = 100, CancellationToken ct = default);
}
