using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IExternalAudiencePolicy
{
    Task<IReadOnlyList<ExternalAudienceTarget>> ResolveTargetsAsync(
        User user,
        string purpose,
        CancellationToken ct = default);
}
