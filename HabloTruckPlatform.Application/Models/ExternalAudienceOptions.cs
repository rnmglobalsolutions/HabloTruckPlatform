using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Models;

public sealed class ExternalAudienceOptions
{
    public string? PreferredProvider { get; set; }

    public string[]? ManyChatPreferredChannels { get; set; }

    public string GetNormalizedPreferredProvider()
    {
        if (string.IsNullOrWhiteSpace(PreferredProvider))
            throw new InvalidOperationException("ExternalAudience:PreferredProvider is required.");

        var provider = PreferredProvider.Trim().ToLowerInvariant();

        return string.Equals(provider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
            ? ExternalIdentityProviders.ManyChat
            : throw new InvalidOperationException($"Unsupported ExternalAudience:PreferredProvider '{PreferredProvider}'.");
    }

    public IReadOnlyList<string> GetNormalizedManyChatPreferredChannels()
    {
        if (ManyChatPreferredChannels is null || ManyChatPreferredChannels.Length == 0)
            throw new InvalidOperationException("ExternalAudience:ManyChatPreferredChannels is required.");

        var normalized = (ManyChatPreferredChannels ?? [])
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Select(ExternalIdentity.NormalizeChannel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            throw new InvalidOperationException("ExternalAudience:ManyChatPreferredChannels must contain at least one non-empty channel.");

        return normalized;
    }
}
