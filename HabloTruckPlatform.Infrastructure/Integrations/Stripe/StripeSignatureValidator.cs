using System.Text;
using HabloTruckPlatform.Application.Config;
using Microsoft.Extensions.Logging;
using Stripe;

namespace HabloTruckPlatform.Infrastructure.Stripe;

public sealed class StripeSignatureValidator
{
    private readonly string _webhookSecret;
    private readonly ILogger<StripeSignatureValidator> _logger;

    public StripeSignatureValidator(AppOptions options, ILogger<StripeSignatureValidator> logger)
    {
        _webhookSecret = options.Stripe.WebhookSecret;
        _logger = logger;
    }

    public Event Validate(string json, string? stripeSignatureHeader)
    {
        if (string.IsNullOrWhiteSpace(stripeSignatureHeader))
            throw new InvalidOperationException("Missing Stripe-Signature header");

        try
        {
            return EventUtility.ConstructEvent(
                json,
                stripeSignatureHeader,
                _webhookSecret,
                throwOnApiVersionMismatch: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stripe signature validation failed");
            throw;
        }
    }
}