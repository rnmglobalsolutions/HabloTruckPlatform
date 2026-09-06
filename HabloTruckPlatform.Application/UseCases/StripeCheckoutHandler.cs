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
    private readonly IAdminPaymentAlertNotifier? _adminPaymentAlerts;
    private readonly ILogger<StripeCheckoutHandler> _logger;

    public StripeCheckoutHandler(
        IStripeCheckoutService stripeCheckoutService,
        StripeOptions stripeOptions,
        ILogger<StripeCheckoutHandler> logger,
        IAdminPaymentAlertNotifier? adminPaymentAlerts = null)
    {
        _stripeCheckoutService = stripeCheckoutService;
        _stripeOptions = stripeOptions;
        _adminPaymentAlerts = adminPaymentAlerts;
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
            return await FailCheckoutAsync("plan_type_required", opWatch, request, ct);

        if (request.Quantity <= 0)
            return await FailCheckoutAsync("quantity_must_be_greater_than_zero", opWatch, request, ct);

        if (string.IsNullOrWhiteSpace(request.SuccessUrl))
            return await FailCheckoutAsync("success_url_required", opWatch, request, ct);

        if (string.IsNullOrWhiteSpace(request.CancelUrl))
            return await FailCheckoutAsync("cancel_url_required", opWatch, request, ct);

        if (!IsAllowedRedirectUrl(request.SuccessUrl, out var successUrlError))
            return await FailCheckoutAsync(successUrlError, opWatch, request, ct);

        if (!IsAllowedRedirectUrl(request.CancelUrl, out var cancelUrlError))
            return await FailCheckoutAsync(cancelUrlError, opWatch, request, ct);

        request.PriceId = GetPriceIdForPlanType(request.PlanType);

        if (string.IsNullOrWhiteSpace(request.PriceId))
            return await FailCheckoutAsync("price_id_not_configured_for_plan", opWatch, request, ct);

        var dependencyWatch = Stopwatch.StartNew();

        try
        {
            var result = await _stripeCheckoutService.CreateCheckoutSessionAsync(request, ct);

            if (result.Result && string.IsNullOrWhiteSpace(result.GeneratedAtUtc))
                result.GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O");

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

            if (!result.Result)
            {
                await NotifyCheckoutFailureAsync(
                    request,
                    string.IsNullOrWhiteSpace(result.Error) ? "stripe_checkout_session_create_failed" : result.Error,
                    ct);
            }

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

            return await FailCheckoutAsync("stripe_checkout_session_create_failed", opWatch, request, ct);
        }
    }

    private async Task<StripeCheckoutSessionResult> FailCheckoutAsync(
        string error,
        Stopwatch opWatch,
        StripeCheckoutSessionRequest request,
        CancellationToken ct)
    {
        await NotifyCheckoutFailureAsync(request, error, ct);
        return Fail(error, opWatch, request.PlanType, request.PriceId);
    }

    private async Task NotifyCheckoutFailureAsync(
        StripeCheckoutSessionRequest request,
        string reason,
        CancellationToken ct)
    {
        if (_adminPaymentAlerts is null)
            return;

        await _adminPaymentAlerts.NotifyAsync(new AdminPaymentAlert
        {
            OperationName = "stripe_checkout_create_session",
            FailureStage = "checkout_link_creation",
            FailureReason = reason,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Email = request.Email,
            PhoneE164 = request.PhoneE164,
            ManyChatSubscriberId = request.ManyChatSubscriberId,
            CompanyId = request.CompanyId,
            PlanType = request.PlanType,
            PriceId = request.PriceId,
            Details =
            {
                ["quantity"] = request.Quantity.ToString(),
                ["successUrl"] = request.SuccessUrl,
                ["cancelUrl"] = request.CancelUrl,
                ["manyChatChannel"] = request.ManyChatChannel,
                ["companyName"] = request.CompanyName,
                ["schoolId"] = request.SchoolId,
                ["cohortId"] = request.CohortId
            }
        }, ct);
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
            GeneratedAtUtc = null,
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
