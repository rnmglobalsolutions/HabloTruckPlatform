using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.Stripex;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IStripeSubscriptionHandler
{
    // Used by webhook (parsed payload)
    Task HandleCheckoutCompletedAsync(StripeEventData data, CancellationToken ct = default);

    Task<AccessDecision?> HandleSubscriptionUpdatedAsync(StripeEventData data, CancellationToken ct = default);

    Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeEventData data, CancellationToken ct = default);

    Task<AccessDecision?> HandleInvoicePaidAsync(StripeEventData data, CancellationToken ct = default);

    Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeEventData data, CancellationToken ct = default);

    // Your existing DTO-based methods (keep them callable from anywhere else)
    Task<AccessDecision?> HandleSubscriptionUpdatedAsync(StripeSubscriptionUpdate input, CancellationToken ct = default);

    Task<AccessDecision?> HandleSubscriptionDeletedAsync(StripeSubscriptionDeleted input, CancellationToken ct = default);

    Task<AccessDecision?> HandleInvoicePaidAsync(StripeInvoicePaid input, CancellationToken ct = default);

    Task<AccessDecision?> HandleInvoicePaymentFailedAsync(StripeInvoicePaymentFailed input, CancellationToken ct = default);
}