namespace HabloTruckPlatform.Application.Integrations.Stripex;

public sealed class StripeOptions
{
    public string? WebhookSecret { get; set; }
    public string? StripeSecretKey { get; set; }
    public string? CustomerPortalConfigurationId { get; set; }
    // Individual
    public string? IndividualMonthlyPriceId { get; set; }
    public string? IndividualYearlyPriceId { get; set; }

    // Fleet seat (per-seat monthly)
    public string? FleetSeatMonthlyPriceId { get; set; }

    // CDL cohort one-time (optional if you use one-time prices)
    public string? CdlCohort25PriceId { get; set; }
    public string? CdlCohort50PriceId { get; set; }
    public string? CdlCohort100PriceId { get; set; }

    // Testing
    public string? TestingPriceId { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WebhookSecret))
            throw new InvalidOperationException("Stripe:WebhookSecret missing.");
        if (string.IsNullOrWhiteSpace(StripeSecretKey))
            throw new InvalidOperationException("Stripe:StripeSecretKey missing.");
        if (string.IsNullOrWhiteSpace(IndividualMonthlyPriceId))
            throw new InvalidOperationException("Stripe:IndividualMonthlyPriceId missing.");
        if (string.IsNullOrWhiteSpace(IndividualYearlyPriceId))
            throw new InvalidOperationException("Stripe:IndividualYearlyPriceId missing.");
        if (string.IsNullOrWhiteSpace(FleetSeatMonthlyPriceId))
            throw new InvalidOperationException("Stripe:FleetSeatMonthlyPriceId missing.");
    }
}
