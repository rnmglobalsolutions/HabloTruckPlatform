using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Stripe.Checkout;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class StripeCheckoutHandler
{
    private readonly IStripeCheckoutService _stripeCheckoutService;
    private readonly StripeOptions _stripeOptions;
    private readonly ILogger<StripeCheckoutHandler> _logger;

    public StripeCheckoutHandler(
        IStripeCheckoutService stripeCheckoutService,
        StripeOptions stripeOptions,
        ILogger<StripeCheckoutHandler> logger)
    {
        _stripeCheckoutService = stripeCheckoutService;
        _stripeOptions = stripeOptions;
        _logger = logger;
    }

    public async Task<StripeCheckoutSessionResult> ExecuteAsync(
        StripeCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail("invalid_request");

        if (string.IsNullOrWhiteSpace(request.PlanType))
            return Fail("plant_type_required");

        if (request.Quantity <= 0)
            return Fail("quantity_must_be_greater_than_zero");

        if (string.IsNullOrWhiteSpace(request.SuccessUrl))
            return Fail("success_url_required");

        if (string.IsNullOrWhiteSpace(request.CancelUrl))
            return Fail("cancel_url_required");

        // var normalizedPlanType = NormalizePlanType(request.PlanType);

        // Optional stricter validation against known catalog
        
        // if (!IsKnownPriceId(request.PriceId))
        // {
        //     _logger.LogWarning("Unknown Stripe price id requested: {PriceId}", request.PriceId);
        // }

        // request.PlanType = normalizedPlanType;

        request.PriceId = GetPriceIdForPlanType(request.PlanType);

        try
        {
            return await _stripeCheckoutService.CreateCheckoutSessionAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CreateStripeCheckoutSessionUseCase failed. PriceId={PriceId}, PlanType={PlanType}",
                request.PriceId,
                request.PlanType);

            return Fail("stripe_checkout_session_create_failed");
        }
    }

    private string NormalizePlanType(string? planType)
    {
        var p = (planType ?? "").Trim().ToLowerInvariant();

        return p switch
        {
            "individual" => "individual",
            "individual_monthly" => "individual",
            "individual_yearly" => "individual",
            "fleet" => "fleet",
            "fleet_seat" => "fleet",
            "company_seat" => "fleet",
            "cdl_cohort" => "cdl_cohort",
            _ => InferPlanTypeFromPriceId(_stripeOptions, p)
        };
    }

    private string GetPriceIdForPlanType(string planType)
    {
        string priceId = string.Empty;
        var pt = (planType ?? "").Trim().ToLowerInvariant();

        switch (pt) {
            case "individual_monthly": 
                priceId = _stripeOptions.IndividualMonthlyPriceId;
                break;
            case "individual_yearly":
                priceId = _stripeOptions.IndividualYearlyPriceId;
                break;
            case "fleet_monthly":
                priceId = _stripeOptions.FleetSeatMonthlyPriceId;
                break;
            case "cdl_cohort_25":
                priceId = _stripeOptions.CdlCohort25PriceId ?? "";
                break;
            case "cdl_cohort_50":
                priceId = _stripeOptions.CdlCohort50PriceId ?? "";
                break;
            case "cdl_cohort_100":
                priceId = _stripeOptions.CdlCohort100PriceId ?? "";
                break;
        }

        return priceId;
    }

    private bool IsKnownPriceId(string priceId)
    {
        return priceId == _stripeOptions.IndividualMonthlyPriceId
            || priceId == _stripeOptions.IndividualYearlyPriceId
            || priceId == _stripeOptions.FleetSeatMonthlyPriceId
            || priceId == _stripeOptions.CdlCohort25PriceId
            || priceId == _stripeOptions.CdlCohort50PriceId
            || priceId == _stripeOptions.CdlCohort100PriceId;
    }

    private static string InferPlanTypeFromPriceId(StripeOptions options, string ignored)
    {
        // fallback only used when planType is empty/unknown;
        // the service still sends metadata with normalized planType if caller provided one.
        return "individual";
    }

    private static StripeCheckoutSessionResult Fail(string error) => new()
    {
        Result = false,
        Url = "",
        SessionId = null,
        Error = error
    };
}