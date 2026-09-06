using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class ExternalAudiencePolicy : IExternalAudiencePolicy
{
    private readonly IExternalIdentityStore _externalIdentityStore;
    private readonly string _preferredProvider;
    private readonly IReadOnlyList<string> _manyChatPreferredChannels;

    public ExternalAudiencePolicy(IExternalIdentityStore externalIdentityStore, ExternalAudienceOptions options)
    {
        _externalIdentityStore = externalIdentityStore;
        ArgumentNullException.ThrowIfNull(options);
        _preferredProvider = options.GetNormalizedPreferredProvider();
        _manyChatPreferredChannels = options.GetNormalizedManyChatPreferredChannels();
    }

    public async Task<IReadOnlyList<ExternalAudienceTarget>> ResolveTargetsAsync(
        User user,
        string purpose,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var manyChatTargets = await ResolvePreferredProviderTargetsAsync(user, ct);
        if (manyChatTargets.Count == 0)
            return [];

        return purpose switch
        {
            ExternalAudiencePurposes.AccessStateSync => manyChatTargets,
            ExternalAudiencePurposes.BillingRecoveryStateSync => manyChatTargets,
            ExternalAudiencePurposes.SubscriptionReminderFlow => [manyChatTargets[0]],
            ExternalAudiencePurposes.PaymentFailedFlow => [manyChatTargets[0]],
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown external audience purpose.")
        };
    }

    private async Task<IReadOnlyList<ExternalAudienceTarget>> ResolvePreferredProviderTargetsAsync(User user, CancellationToken ct)
    {
        var legacySubscriberId = NormalizeSubscriberId(user.ManyChatSubscriberId);
        var identities = await _externalIdentityStore.ListByUserAsync(user.UserId, ct);

        var ordered = identities
            .Where(identity =>
                string.Equals(identity.Provider, _preferredProvider, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(identity.ExternalSubject))
            .OrderByDescending(identity => identity.IsPrimary)
            .ThenBy(identity => GetChannelPriority(identity.Channel))
            .ThenByDescending(identity =>
                string.Equals(
                    NormalizeSubscriberId(identity.ExternalSubject),
                    legacySubscriberId,
                    StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(identity => identity.LastSeenAtUtc)
            .ThenByDescending(identity => identity.CreatedAtUtc)
            .Select(identity => new ExternalAudienceTarget(
                Provider: identity.Provider,
                ExternalSubject: identity.ExternalSubject.Trim(),
                Channel: ExternalIdentity.NormalizeChannel(identity.Channel),
                IsPrimary: identity.IsPrimary,
                CreatedAtUtc: identity.CreatedAtUtc,
                LastSeenAtUtc: identity.LastSeenAtUtc))
            .Distinct(ExternalAudienceTargetExternalSubjectComparer.Instance)
            .ToList();

        if (ordered.Count == 0 && !string.IsNullOrWhiteSpace(legacySubscriberId))
        {
            ordered.Add(new ExternalAudienceTarget(
                Provider: _preferredProvider,
                ExternalSubject: legacySubscriberId,
                Channel: ExternalIdentityChannels.Unknown,
                IsPrimary: true,
                CreatedAtUtc: user.UpdatedAtUtc ?? DateTimeOffset.UtcNow,
                LastSeenAtUtc: user.UpdatedAtUtc ?? DateTimeOffset.UtcNow));
        }

        return ordered;
    }

    private static string? NormalizeSubscriberId(string? subscriberId)
        => string.IsNullOrWhiteSpace(subscriberId) ? null : subscriberId.Trim();

    private int GetChannelPriority(string? channel)
    {
        var normalizedChannel = ExternalIdentity.NormalizeChannel(channel);

        for (var i = 0; i < _manyChatPreferredChannels.Count; i++)
        {
            if (string.Equals(_manyChatPreferredChannels[i], normalizedChannel, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }

    private sealed class ExternalAudienceTargetExternalSubjectComparer : IEqualityComparer<ExternalAudienceTarget>
    {
        public static readonly ExternalAudienceTargetExternalSubjectComparer Instance = new();

        public bool Equals(ExternalAudienceTarget? x, ExternalAudienceTarget? y)
            => string.Equals(x?.Provider, y?.Provider, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x?.ExternalSubject, y?.ExternalSubject, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ExternalAudienceTarget obj)
            => StringComparer.OrdinalIgnoreCase.GetHashCode($"{obj.Provider}|{obj.ExternalSubject}");
    }
}
