
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
    /// Update lookup mappings (email/subscriber/stripe customer) -> (UserPk, UserId).
    /// Should be idempotent.
    /// </summary>
    Task UpsertLookupsAsync(User user, CancellationToken ct = default);
}