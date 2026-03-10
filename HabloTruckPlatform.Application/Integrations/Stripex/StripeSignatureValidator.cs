using Microsoft.Extensions.Logging;
using Stripe;

namespace HabloTruckPlatform.Application.Integrations.Stripex
{
    public sealed class StripeSignatureValidator
    {
        private readonly string _webhookSecret;
        private readonly ILogger<StripeSignatureValidator> _logger;

        public StripeSignatureValidator(StripeOptions options, ILogger<StripeSignatureValidator> logger)
        {
            _logger = logger;
            _webhookSecret = options.WebhookSecret ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_webhookSecret))
            {
                _logger.LogError("Stripe WebhookSecret is not configured. Check the app settings.");
                throw new InvalidOperationException("Stripe WebhookSecret is not configured.");
            }
        }

        public Event Validate(string json, string? stripeSignatureHeader)
        {
            if (string.IsNullOrWhiteSpace(stripeSignatureHeader))
            {
                _logger.LogWarning("Missing Stripe-Signature header in webhook request");
                throw new InvalidOperationException("Missing Stripe-Signature header");
            }

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
}
