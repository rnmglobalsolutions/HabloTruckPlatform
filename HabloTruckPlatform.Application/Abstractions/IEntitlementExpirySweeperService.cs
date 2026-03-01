using System;
using System.Collections.Generic;
using System.Text;

namespace HabloTruckPlatform.Application.Abstractions
{
    public interface IEntitlementExpirySweeperService
    {
        Task SweepAsync(DateTime utcNow, CancellationToken ct);
    }
}
