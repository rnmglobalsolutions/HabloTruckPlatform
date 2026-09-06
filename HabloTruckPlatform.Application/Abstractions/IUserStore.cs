using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IUserStore
{
    Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default);

    Task UpsertAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Create user if it does not exist; returns the created or existing user.
    /// Implementations should guarantee no duplicates for the same identity.
    /// </summary>
    Task<User> GetOrCreateAsync(
        string? emailNormalized,
        string? manyChatSubscriberId,
        string? phoneE164,
        CancellationToken ct = default);

    /// <summary>
    /// Create or resolve a user from a generic external identity plus fallback contact attributes.
    /// </summary>
    Task<User> GetOrCreateByExternalIdentityAsync(
        string? emailNormalized,
        string? phoneE164,
        string? provider,
        string? externalSubject,
        string? channel,
        CancellationToken ct = default)
        => GetOrCreateAsync(
            emailNormalized,
            string.Equals(provider?.Trim(), HabloTruckPlatform.Domain.Models.ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
                ? externalSubject
                : null,
            phoneE164,
            ct);

    /// <summary>
    /// Update lookup mappings (email/subscriber/stripe customer) -> (UserPk, UserId).
    /// Should be idempotent.
    /// </summary>
    Task UpsertLookupsAsync(User user, CancellationToken ct = default);
    Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(
        int take = 500, CancellationToken ct = default);
    Task<StripeUserScanPage> QueryUsersWithStripePageAsync(
        int take = 500,
        int startBucket = 0,
        CancellationToken ct = default);
}
