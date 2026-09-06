using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class ManyChatAudienceResolver : IManyChatAudienceResolver
{
    private readonly IExternalAudiencePolicy _externalAudiencePolicy;

    public ManyChatAudienceResolver(IExternalAudiencePolicy externalAudiencePolicy)
    {
        _externalAudiencePolicy = externalAudiencePolicy;
    }

    public async Task<IReadOnlyList<string>> ResolveStateSyncSubscriberIdsAsync(User user, CancellationToken ct = default)
        => (await _externalAudiencePolicy.ResolveTargetsAsync(user, ExternalAudiencePurposes.AccessStateSync, ct))
            .Select(target => target.ExternalSubject)
            .ToList();

    public async Task<string?> ResolvePreferredSubscriberIdAsync(User user, string purpose, CancellationToken ct = default)
    {
        var targets = await _externalAudiencePolicy.ResolveTargetsAsync(user, purpose, ct);
        return targets.Count == 0 ? null : targets[0].ExternalSubject;
    }
}
