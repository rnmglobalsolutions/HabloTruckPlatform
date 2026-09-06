namespace HabloTruckPlatform.Application.Models;

public sealed record StripeOpenInvoiceRetryAttempt(
    string CustomerId,
    string SubscriptionId,
    string? InvoiceId,
    string? InvoiceStatus,
    string? CollectionMethod,
    bool InvoiceFound,
    bool PaymentAttempted,
    bool InvoicePaid);
