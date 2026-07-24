namespace HabloTruckPlatform.Application.Models;

public enum AdminPaymentAlertDeliveryStatus
{
    Sent,
    Skipped,
    Failed
}

public sealed record AdminPaymentAlertDeliveryResult(
    AdminPaymentAlertDeliveryStatus Status,
    string Reason,
    int? StatusCode = null)
{
    public static AdminPaymentAlertDeliveryResult Sent(string reason = "sendgrid_accepted", int? statusCode = null)
        => new(AdminPaymentAlertDeliveryStatus.Sent, reason, statusCode);

    public static AdminPaymentAlertDeliveryResult Skipped(string reason)
        => new(AdminPaymentAlertDeliveryStatus.Skipped, reason);

    public static AdminPaymentAlertDeliveryResult Failed(string reason, int? statusCode = null)
        => new(AdminPaymentAlertDeliveryStatus.Failed, reason, statusCode);
}
