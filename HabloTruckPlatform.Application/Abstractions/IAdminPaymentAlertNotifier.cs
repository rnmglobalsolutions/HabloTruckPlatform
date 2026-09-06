using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IAdminPaymentAlertNotifier
{
    Task<AdminPaymentAlertDeliveryResult> NotifyAsync(AdminPaymentAlert alert, CancellationToken ct = default);
}
