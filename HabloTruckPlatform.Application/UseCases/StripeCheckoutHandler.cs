using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Stripe.Checkout;
using System.Diagnostics;

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
        var opWatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} PlanType={PlanType} Quantity={Quantity}",
            "entry",
            "stripe_checkout_create_session",
            request?.PlanType,
            request?.Quantity);

        if (request is null)
            return Fail("invalid_request", opWatch, null, null);

        if (string.IsNullOrWhiteSpace(request.PlanType))
            return Fail("plan_type_required", opWatch, request.PlanType, request.PriceId);

        if (request.Quantity <= 0)
            return Fail("quantity_must_be_greater_than_zero", opWatch, request.PlanType, request.PriceId);

        if (string.IsNullOrWhiteSpace(request.SuccessUrl))
            return Fail("success_url_required", opWatch, request.PlanType, request.PriceId);

        if (string.IsNullOrWhiteSpace(request.CancelUrl))
            return Fail("cancel_url_required", opWatch, request.PlanType, request.PriceId);

        if (!IsAllowedRedirectUrl(request.SuccessUrl, out var successUrlError))
            return Fail(successUrlError, opWatch, request.PlanType, request.PriceId);

        if (!IsAllowedRedirectUrl(request.CancelUrl, out var cancelUrlError))
            return Fail(cancelUrlError, opWatch, request.PlanType, request.PriceId);

        request.PriceId = GetPriceIdForPlanType(request.PlanType);

        if (string.IsNullOrWhiteSpace(request.PriceId))
            return Fail("price_id_not_configured_for_plan", opWatch, request.PlanType, request.PriceId);

        var dependencyWatch = Stopwatch.StartNew();

        try
        {
            var result = await _stripeCheckoutService.CreateCheckoutSessionAsync(request, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "stripe",
                "checkout_session_create",
                "Stripe API",
                dependencyWatch.ElapsedMilliseconds,
                result.Result);

            _logger.LogInformation(
                "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} PlanType={PlanType} PriceId={PriceId} DurationMs={DurationMs}",
                "outcome",
                result.Result ? "completed" : "dependency_failed",
                result.Result ? "checkout_session_created" : result.Error,
                request.PlanType,
                request.PriceId,
                opWatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} PlanType={PlanType} PriceId={PriceId} DurationMs={DurationMs}",
                "exception",
                "dependency_failed",
                "stripe",
                "checkout_session_create",
                request.PlanType,
                request.PriceId,
                dependencyWatch.ElapsedMilliseconds);

            return Fail("stripe_checkout_session_create_failed", opWatch, request.PlanType, request.PriceId);
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
            "cdl_english_cohort" => "cdl_english_cohort",
            _ => InferPlanTypeFromPriceId(_stripeOptions, p)
        };
    }

    private string GetPriceIdForPlanType(string planType)
    {
        string priceId = string.Empty;
        var pt = (planType ?? "").Trim().ToLowerInvariant();

        switch (pt)
        {
            case "individual_monthly":
                priceId = _stripeOptions.IndividualMonthlyPriceId ?? string.Empty;
                break;
            case "individual_yearly":
                priceId = _stripeOptions.IndividualYearlyPriceId ?? string.Empty;
                break;
            case "fleet":
            case "fleet_seat":
            case "company_seat":
            case "fleet_monthly":
                priceId = _stripeOptions.FleetSeatMonthlyPriceId ?? string.Empty;
                break;
            case "cdl_cohort_25":
                priceId = _stripeOptions.CdlCohort25PriceId ?? string.Empty;
                break;
            case "cdl_cohort_50":
                priceId = _stripeOptions.CdlCohort50PriceId ?? string.Empty;
                break;
            case "cdl_cohort_100":
                priceId = _stripeOptions.CdlCohort100PriceId ?? string.Empty;
                break;
            case "cdl_english_cohort":
                priceId = _stripeOptions.CdlEnglishCohortPriceId ?? string.Empty;
                break;
            case "testing":
                priceId = _stripeOptions.TestingPriceId ?? string.Empty;
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
            || priceId == _stripeOptions.CdlCohort100PriceId
            || priceId == _stripeOptions.CdlEnglishCohortPriceId;
    }

    private static string InferPlanTypeFromPriceId(StripeOptions options, string ignored)
    {
        // fallback only used when planType is empty/unknown;
        // the service still sends metadata with normalized planType if caller provided one.
        return "individual";
    }

    private StripeCheckoutSessionResult Fail(string error, Stopwatch opWatch, string? planType, string? priceId)
    {
        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} PlanType={PlanType} PriceId={PriceId} DurationMs={DurationMs}",
            "outcome",
            "validation_failed",
            error,
            planType,
            priceId,
            opWatch.ElapsedMilliseconds);

        return new StripeCheckoutSessionResult
        {
            Result = false,
            Url = "",
            SessionId = null,
            Error = error
        };
    }

    private bool IsAllowedRedirectUrl(string url, out string error)
    {
        error = "invalid_redirect_url";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "redirect_url_invalid";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "redirect_url_must_use_https";
            return false;
        }

        var allowedHosts = (_stripeOptions.AllowedCheckoutRedirectHosts ?? [])
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allowedHosts.Count == 0)
            return true;

        if (!allowedHosts.Contains(uri.Host.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase))
        {
            error = "redirect_url_host_not_allowed";
            return false;
        }

        return true;
    }
}
