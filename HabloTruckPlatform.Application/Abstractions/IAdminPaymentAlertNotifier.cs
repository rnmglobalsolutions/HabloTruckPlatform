using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IAdminPaymentAlertNotifier
{
    Task NotifyAsync(AdminPaymentAlert alert, CancellationToken ct = default);
}
