using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Infrastructure.Telemetry;

public static class LogContext
{
    public static IDisposable BeginUserScope(
        ILogger logger,
        string? userId = null,
        string? companyId = null,
        string? stripeCustomerId = null)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = userId,
            ["CompanyId"] = companyId,
            ["StripeCustomerId"] = stripeCustomerId
        });
    }
}