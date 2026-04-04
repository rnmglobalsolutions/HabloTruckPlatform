namespace HabloTruckPlatform.Domain.Models;

public sealed class ExternalIdentity
{
    public required string UserId { get; init; }
    public required string Provider { get; init; }
    public required string ExternalSubject { get; init; }

    public string? Channel { get; init; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public string? MetadataJson { get; set; }

    public static string NormalizeProvider(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));

        return provider.Trim().ToLowerInvariant();
    }

    public static string NormalizeExternalSubject(string externalSubject)
    {
        if (string.IsNullOrWhiteSpace(externalSubject))
            throw new ArgumentException("ExternalSubject is required.", nameof(externalSubject));

        return externalSubject.Trim();
    }

    public static string NormalizeChannel(string? channel)
        => string.IsNullOrWhiteSpace(channel) ? ExternalIdentityChannels.Unknown : channel.Trim().ToLowerInvariant();
}

public static class ExternalIdentityProviders
{
    public const string ManyChat = "manychat";
    public const string MobileApp = "mobile_app";
    public const string WebApp = "web_app";
}

public static class ExternalIdentityChannels
{
    public const string Unknown = "unknown";
    public const string Facebook = "facebook";
    public const string Instagram = "instagram";
    public const string WhatsApp = "whatsapp";
    public const string Ios = "ios";
    public const string Android = "android";
    public const string Web = "web";
}
