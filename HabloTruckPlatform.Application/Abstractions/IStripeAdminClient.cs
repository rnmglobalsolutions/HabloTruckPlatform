using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IStripeAdminClient
{
    Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default);
    Task<StripeEventData?> GetEventDataAsync(
        string eventId, CancellationToken ct = default);
    Task<StripePaymentMethodUpdateSession> CreatePaymentMethodUpdateSessionAsync(
        string customerId,
        string? subscriptionId,
        string returnUrl,
        CancellationToken ct = default);
    Task<StripeOpenInvoiceRetryAttempt> RetryOpenInvoiceAsync(
        string customerId,
        string subscriptionId,
        CancellationToken ct = default);
}
