using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IManyChatAudienceResolver
{
    Task<IReadOnlyList<string>> ResolveStateSyncSubscriberIdsAsync(User user, CancellationToken ct = default);
    Task<string?> ResolvePreferredSubscriberIdAsync(User user, string purpose, CancellationToken ct = default);
}
