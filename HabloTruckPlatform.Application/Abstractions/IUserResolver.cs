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
    /// Resolve a user reference from normalized email.
    /// </summary>
    Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default);
}