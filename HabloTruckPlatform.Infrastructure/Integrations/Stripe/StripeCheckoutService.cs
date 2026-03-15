using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using Stripe.Checkout;
using System.Diagnostics;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeCheckoutService : IStripeCheckoutService
{
    private readonly ILogger<StripeCheckoutService> _logger;

    public StripeCheckoutService(ILogger<StripeCheckoutService>? logger = null)
    {
        _logger = logger ?? NullLogger<StripeCheckoutService>.Instance;
    }

    public async Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
        StripeCheckoutSessionRequest request,
        CancellationToken ct = default)
    {
        var options = BuildSessionCreateOptions(request);

        var service = new SessionService();
        var watch = Stopwatch.StartNew();
        var session = await service.CreateAsync(options, cancellationToken: ct);

        _logger.LogDebug(
            "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} SessionId={SessionId}",
            "dependency",
            "stripe",
            "checkout_session_create",
            "Stripe API",
            watch.ElapsedMilliseconds,
            session is not null,
            session?.Id);

        return new StripeCheckoutSessionResult
        {
            Result = true,
            Url = session?.Url ?? "",
            SessionId = session?.Id,
            Error = null
        };
    }

    internal static SessionCreateOptions BuildSessionCreateOptions(StripeCheckoutSessionRequest request)
    {
        var metadata = BuildMetadata(request);

        return new SessionCreateOptions
        {
            Mode = "subscription",
            ClientReferenceId = request.ManyChatSubscriberId,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
            BillingAddressCollection = "required",
            CustomerEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            // Keep initial signup on instant-confirmation methods so webhook-driven FULL access
            // remains aligned with actual successful payment completion.
            PaymentMethodTypes = new List<string>
            {
                "card",
                "link"
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
