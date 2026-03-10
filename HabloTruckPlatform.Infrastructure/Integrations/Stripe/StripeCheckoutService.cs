using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using Stripe;
using Stripe.Checkout;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeCheckoutService : IStripeCheckoutService
{
    public async Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
        StripeCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        var metadata = BuildMetadata(request);

        // StripeConfiguration.ApiKey = _config["STRIPE_SECRET_KEY"];

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            ClientReferenceId = request.ManyChatSubscriberId,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            BillingAddressCollection = "required",
            CustomerEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            PaymentMethodTypes = new List<string>
            {
                "card",       // Includes Apple Pay and Google Pay when configured
                "link",       // Stripe Link payments
                "us_bank_account" // ACH Direct Debit if enabled for your account
            },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Price = request.PriceId.Trim(),
                    Quantity = request.Quantity
                }
            },
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = metadata
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, cancellationToken: ct);

        return new StripeCheckoutSessionResult
        {
            Result = true,
            Url = session.Url ?? "",
            SessionId = session.Id,
            Error = null
        };
    }

    private static Dictionary<string, string> BuildMetadata(StripeCheckoutSessionRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        AddIfNotBlank(metadata, "planType", request.PlanType);
        AddIfNotBlank(metadata, "companyId", request.CompanyId);
        AddIfNotBlank(metadata, "companyName", request.CompanyName);
        AddIfNotBlank(metadata, "ht_company_id", request.CompanyId);
        AddIfNotBlank(metadata, "ht_school_id", request.SchoolId);
        AddIfNotBlank(metadata, "ht_cohort", request.CohortId);
        AddIfNotBlank(metadata, "email", request.Email);
        AddIfNotBlank(metadata, "phone", request.PhoneE164);
        AddIfNotBlank(metadata, "manychatSubscriberId", request.ManyChatSubscriberId);

        if (request.Seats > 0)
            metadata["seats"] = request.Seats.ToString();

        if (request.DurationDays > 0)
            metadata["durationDays"] = request.DurationDays.ToString();

        return metadata;
    }

    private static void AddIfNotBlank(IDictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value.Trim();
    }
}