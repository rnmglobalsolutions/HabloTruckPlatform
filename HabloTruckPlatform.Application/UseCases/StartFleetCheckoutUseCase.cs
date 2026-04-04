using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Ids;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class StartFleetCheckoutUseCase
{
    private readonly StripeCheckoutHandler _stripeCheckoutHandler;

    public StartFleetCheckoutUseCase(StripeCheckoutHandler stripeCheckoutHandler)
    {
        _stripeCheckoutHandler = stripeCheckoutHandler;
    }

    public async Task<StartFleetCheckoutResult> ExecuteAsync(
        StartFleetCheckoutRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail("invalid_request");

        var companyName = request.CompanyName?.Trim();
        var companyId = string.IsNullOrWhiteSpace(request.CompanyId)
            ? $"C_{UlidIds.NewCompanyId()}"
            : request.CompanyId.Trim();
        var email = Normalize(request.Email);
        var phoneE164 = Normalize(request.PhoneE164);
        var manyChatSubscriberId = Normalize(request.ManyChatSubscriberId);
        var manyChatChannel = Normalize(request.ManyChatChannel);
        var successUrl = Normalize(request.SuccessUrl);
        var cancelUrl = Normalize(request.CancelUrl);

        if (string.IsNullOrWhiteSpace(companyName))
            return Fail("company_name_required", companyId, companyName, request.Seats);

        if (string.IsNullOrWhiteSpace(email))
            return Fail("email_required", companyId, companyName, request.Seats);

        if (request.Seats <= 0)
            return Fail("seats_must_be_greater_than_zero", companyId, companyName, request.Seats);

        if (string.IsNullOrWhiteSpace(successUrl))
            return Fail("success_url_required", companyId, companyName, request.Seats);

        if (string.IsNullOrWhiteSpace(cancelUrl))
            return Fail("cancel_url_required", companyId, companyName, request.Seats);

        var checkout = await _stripeCheckoutHandler.ExecuteAsync(new StripeCheckoutSessionRequest
        {
            PlanType = "fleet",
            Quantity = request.Seats,
            Seats = request.Seats,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            CompanyId = companyId,
            CompanyName = companyName,
            Email = email,
            PhoneE164 = phoneE164,
            ManyChatSubscriberId = manyChatSubscriberId,
            ManyChatChannel = manyChatChannel
        }, ct);

        return new StartFleetCheckoutResult
        {
            Result = checkout.Result,
            Url = checkout.Url,
            SessionId = checkout.SessionId,
            Error = checkout.Error,
            CompanyId = companyId,
            CompanyName = companyName,
            Seats = request.Seats
        };
    }

    private static StartFleetCheckoutResult Fail(string error, string? companyId = null, string? companyName = null, int seats = 0)
        => new()
        {
            Result = false,
            Error = error,
            CompanyId = companyId,
            CompanyName = companyName,
            Seats = seats
        };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
