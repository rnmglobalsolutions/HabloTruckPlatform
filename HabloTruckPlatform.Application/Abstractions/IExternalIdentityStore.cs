using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IExternalIdentityStore
{
    Task<ExternalIdentity?> GetAsync(string provider, string externalSubject, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalIdentity>> ListByUserAsync(string userId, CancellationToken ct = default);
    Task UpsertAsync(ExternalIdentity identity, CancellationToken ct = default);
}
