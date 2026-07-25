namespace HabloTruckPlatform.Infrastructure.Integrations.Email;

public sealed class AdminPaymentAlertEmailOptions
{
    public bool Enabled { get; set; }
    public string EnvironmentName { get; set; } = "Production";
    public string ToEmail { get; set; } = "grettadecien@gmail.com";
    public string FromEmail { get; set; } = "info@rnmglobalsolutions.com";
    public string FromName { get; set; } = "HabloTruck Production Alerts";
    public string SendGridApiKey { get; set; } = "";
}
