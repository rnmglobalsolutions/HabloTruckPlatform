using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class ManyChatAudienceResolverTests
{
    [Fact]
    public async Task ResolveStateSyncSubscriberIdsAsync_Should_ReturnAllManyChatContacts_WithPreferredFirst()
    {
        var store = new InMemoryExternalIdentityStore();
        var now = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            UserId = "U_audience",
            ManyChatSubscriberId = "sid_latest"
        };

        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_facebook",
            Channel = ExternalIdentityChannels.Facebook,
            IsPrimary = false,
            CreatedAtUtc = now.AddMinutes(-10),
            LastSeenAtUtc = now.AddMinutes(-10)
        });
        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_latest",
            Channel = ExternalIdentityChannels.Instagram,
            IsPrimary = true,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        });
        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.WebApp,
            ExternalSubject = "web_user_123",
            Channel = ExternalIdentityChannels.Web,
            IsPrimary = true,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        });

        var sut = new ManyChatAudienceResolver(new ExternalAudiencePolicy(store, CreateDefaultOptions()));

        var subscriberIds = await sut.ResolveStateSyncSubscriberIdsAsync(user);
        var preferred = await sut.ResolvePreferredSubscriberIdAsync(user, ExternalAudiencePurposes.SubscriptionReminderFlow);

        Assert.Equal(["sid_latest", "sid_facebook"], subscriberIds);
        Assert.Equal("sid_latest", preferred);
    }

    [Fact]
    public async Task ResolvePreferredSubscriberIdAsync_Should_PreferExternalIdentityOverStaleLegacyValue()
    {
        var store = new InMemoryExternalIdentityStore();
        var now = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            UserId = "U_stale_legacy",
            ManyChatSubscriberId = "sid_stale_legacy"
        };

        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_real_current",
            Channel = ExternalIdentityChannels.WhatsApp,
            IsPrimary = true,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        });

        var sut = new ManyChatAudienceResolver(new ExternalAudiencePolicy(store, CreateDefaultOptions()));

        var subscriberIds = await sut.ResolveStateSyncSubscriberIdsAsync(user);
        var preferred = await sut.ResolvePreferredSubscriberIdAsync(user, ExternalAudiencePurposes.SubscriptionReminderFlow);

        Assert.Equal(["sid_real_current"], subscriberIds);
        Assert.Equal("sid_real_current", preferred);
    }

    [Fact]
    public async Task ExternalAudiencePolicy_Should_ReturnAllManyChatTargetsForStateSync_ButOnlyPreferredForVisibleFlows()
    {
        var store = new InMemoryExternalIdentityStore();
        var now = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            UserId = "U_policy",
            ManyChatSubscriberId = "sid_primary"
        };

        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_secondary",
            Channel = ExternalIdentityChannels.Facebook,
            IsPrimary = false,
            CreatedAtUtc = now.AddMinutes(-5),
            LastSeenAtUtc = now.AddMinutes(-5)
        });
        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_primary",
            Channel = ExternalIdentityChannels.Instagram,
            IsPrimary = true,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        });

        var sut = new ExternalAudiencePolicy(store, CreateDefaultOptions());

        var stateTargets = await sut.ResolveTargetsAsync(user, ExternalAudiencePurposes.AccessStateSync);
        var reminderTargets = await sut.ResolveTargetsAsync(user, ExternalAudiencePurposes.SubscriptionReminderFlow);

        Assert.Equal(["sid_primary", "sid_secondary"], stateTargets.Select(x => x.ExternalSubject).ToArray());
        Assert.Equal(["sid_primary"], reminderTargets.Select(x => x.ExternalSubject).ToArray());
    }

    [Fact]
    public async Task ExternalAudiencePolicy_Should_UseConfiguredManyChatChannelPriority_WhenNoPrimaryExists()
    {
        var store = new InMemoryExternalIdentityStore();
        var now = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            UserId = "U_channel_preference",
            ManyChatSubscriberId = "sid_instagram"
        };

        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_instagram",
            Channel = ExternalIdentityChannels.Instagram,
            IsPrimary = false,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        });
        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_whatsapp",
            Channel = ExternalIdentityChannels.WhatsApp,
            IsPrimary = false,
            CreatedAtUtc = now.AddMinutes(-1),
            LastSeenAtUtc = now.AddMinutes(-1)
        });

        var options = new ExternalAudienceOptions
        {
            PreferredProvider = ExternalIdentityProviders.ManyChat,
            ManyChatPreferredChannels =
            [
                ExternalIdentityChannels.WhatsApp,
                ExternalIdentityChannels.Instagram,
                ExternalIdentityChannels.Facebook
            ]
        };

        var sut = new ExternalAudiencePolicy(store, options);

        var reminderTargets = await sut.ResolveTargetsAsync(user, ExternalAudiencePurposes.SubscriptionReminderFlow);
        var stateTargets = await sut.ResolveTargetsAsync(user, ExternalAudiencePurposes.AccessStateSync);

        Assert.Equal(["sid_whatsapp"], reminderTargets.Select(x => x.ExternalSubject).ToArray());
        Assert.Equal(["sid_whatsapp", "sid_instagram"], stateTargets.Select(x => x.ExternalSubject).ToArray());
    }

    [Fact]
    public async Task ExternalAudiencePolicy_Should_Throw_ForUnknownPurpose()
    {
        var store = new InMemoryExternalIdentityStore();
        var now = new DateTimeOffset(2026, 4, 3, 12, 0, 0, TimeSpan.Zero);
        var user = new User
        {
            UserId = "U_unknown_purpose",
            ManyChatSubscriberId = "sid_primary"
        };

        store.Add(new ExternalIdentity
        {
            UserId = user.UserId,
            Provider = ExternalIdentityProviders.ManyChat,
            ExternalSubject = "sid_primary",
            Channel = ExternalIdentityChannels.Instagram,
            IsPrimary = true,
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        });

        var sut = new ExternalAudiencePolicy(store, CreateDefaultOptions());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.ResolveTargetsAsync(user, "unknown-purpose"));
    }

    private sealed class InMemoryExternalIdentityStore : IExternalIdentityStore
    {
        private readonly List<ExternalIdentity> _items = new();

        public void Add(ExternalIdentity identity) => _items.Add(identity);

        public Task<ExternalIdentity?> GetAsync(string provider, string externalSubject, CancellationToken ct = default)
            => Task.FromResult(_items.FirstOrDefault(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ExternalSubject, externalSubject, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<ExternalIdentity>> ListByUserAsync(string userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExternalIdentity>>(
                _items.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task UpsertAsync(ExternalIdentity identity, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static ExternalAudienceOptions CreateDefaultOptions()
        => new()
        {
            PreferredProvider = ExternalIdentityProviders.ManyChat,
            ManyChatPreferredChannels =
            [
                ExternalIdentityChannels.Facebook,
                ExternalIdentityChannels.WhatsApp,
                ExternalIdentityChannels.Instagram,
                ExternalIdentityChannels.Unknown
            ]
        };
}
