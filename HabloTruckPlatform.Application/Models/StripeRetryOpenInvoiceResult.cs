namespace HabloTruckPlatform.Application.Models;

public sealed class StripeRetryOpenInvoiceResult
{
    public bool Result { get; set; }
    public string? CustomerId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? InvoiceId { get; set; }
    public string? InvoiceStatus { get; set; }
    public string? CollectionMethod { get; set; }
    public bool InvoiceFound { get; set; }
    public bool PaymentAttempted { get; set; }
    public bool InvoicePaid { get; set; }
    public string? Error { get; set; }
}
