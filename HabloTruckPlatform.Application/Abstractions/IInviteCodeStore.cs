using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IInviteCodeStore
{
    Task<InviteCode?> GetAsync(string companyId, string code, CancellationToken ct = default);
    Task CreateAsync(InviteCode invite, CancellationToken ct = default);
    Task UpsertAsync(InviteCode invite, CancellationToken ct = default);
}