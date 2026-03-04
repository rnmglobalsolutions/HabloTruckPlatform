namespace HabloTruckPlatform.Application.Integrations.Stripex;

public sealed class StripeOptions
{
    public required string WebhookSecret { get; init; }
    // Individual
    public string IndividualMonthlyPriceId { get; set; } = "";
    public string IndividualYearlyPriceId { get; set; } = "";

    // Fleet seat (per-seat monthly)
    public string FleetSeatMonthlyPriceId { get; set; } = "";

    // CDL cohort one-time (optional if you use one-time prices)
    public string? CdlCohort25PriceId { get; set; }
    public string? CdlCohort50PriceId { get; set; }
    public string? CdlCohort100PriceId { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(IndividualMonthlyPriceId))
            throw new InvalidOperationException("StripePriceCatalog:IndividualMonthlyPriceId missing.");
        if (string.IsNullOrWhiteSpace(IndividualYearlyPriceId))
            throw new InvalidOperationException("StripePriceCatalog:IndividualYearlyPriceId missing.");
        if (string.IsNullOrWhiteSpace(FleetSeatMonthlyPriceId))
            throw new InvalidOperationException("StripePriceCatalog:FleetSeatMonthlyPriceId missing.");
    }
}