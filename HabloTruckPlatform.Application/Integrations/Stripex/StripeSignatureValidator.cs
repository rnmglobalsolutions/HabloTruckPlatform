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
            _webhookSecret = options.WebhookSecret;
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
}
