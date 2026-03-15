using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Infrastructure.Stripe;

namespace HabloTruckPlatform.Domain.Tests.Infrastructure;

public sealed class StripeCheckoutServiceTests
{
    [Fact]
    public void BuildSessionCreateOptions_Should_UseOnlyInstantConfirmationPaymentMethods_ForInitialSignup()
    {
        var request = new StripeCheckoutSessionRequest
        {
            PriceId = "price_123",
            Quantity = 1,
            SuccessUrl = "https://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel",
            PlanType = "individual_monthly",
            Email = "driver@hablotruck.com",
            ManyChatSubscriberId = "sid_123"
        };

        var options = StripeCheckoutService.BuildSessionCreateOptions(request);

        Assert.Equal(new[] { "card", "link" }, options.PaymentMethodTypes);
        Assert.DoesNotContain("us_bank_account", options.PaymentMethodTypes);
    }
}
