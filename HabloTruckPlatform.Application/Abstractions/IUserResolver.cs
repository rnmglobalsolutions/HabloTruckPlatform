using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IUserResolver
{
    /// <summary>
    /// Resolve a user reference from Stripe Customer Id.
    /// </summary>
    Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default);

    /// <summary>
    /// Resolve a user reference from ManyChat Subscriber Id.
    /// </summary>
    Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default);

    /// <summary>
    /// Resolve a user reference from a generic external identity.
    /// </summary>
    Task<UserRef?> ResolveByExternalIdentityAsync(string provider, string externalSubject, CancellationToken ct = default)
        => string.Equals(provider?.Trim(), HabloTruckPlatform.Domain.Models.ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
            ? ResolveByManyChatSubscriberIdAsync(externalSubject, ct)
            : Task.FromResult<UserRef?>(null);

    /// <summary>
    /// Resolve a user reference from normalized email.
    /// </summary>
    Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default);

    /// <summary>
    /// Resolve a user reference from normalized phone in E.164 format.
    /// </summary>
    Task<UserRef?> ResolveByPhoneE164Async(string phoneE164, CancellationToken ct = default)
        => Task.FromResult<UserRef?>(null);
}
