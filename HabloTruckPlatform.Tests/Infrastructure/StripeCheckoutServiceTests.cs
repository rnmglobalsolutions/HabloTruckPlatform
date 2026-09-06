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

    [Fact]
    public void BuildSessionCreateOptions_Should_UsePaymentMode_ForCdlEnglishCohort()
    {
        var request = new StripeCheckoutSessionRequest
        {
            PriceId = "price_cdl_english",
            Quantity = 1,
            SuccessUrl = "https://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel",
            PlanType = "cdl_english_cohort",
            Email = "driver@hablotruck.com",
            ManyChatSubscriberId = "sid_123",
            CohortId = "CDL_EN_2026_01"
        };

        var options = StripeCheckoutService.BuildSessionCreateOptions(request);

        Assert.Equal("payment", options.Mode);
        Assert.Equal("always", options.CustomerCreation);
        Assert.Null(options.SubscriptionData);
    }

    [Fact]
    public void BuildSessionCreateOptions_Should_UsePaymentMode_ForCdlCohortPack()
    {
        var request = new StripeCheckoutSessionRequest
        {
            PriceId = "price_cdl_25",
            Quantity = 25,
            SuccessUrl = "https://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel",
            PlanType = "cdl_cohort_25",
            CompanyId = "SCH_001",
            CompanyName = "Roadmaster",
            Seats = 25
        };

        var options = StripeCheckoutService.BuildSessionCreateOptions(request);

        Assert.Equal("payment", options.Mode);
        Assert.Equal("always", options.CustomerCreation);
        Assert.Null(options.SubscriptionData);
    }

    [Fact]
    public void BuildSessionCreateOptions_Should_IncludeManyChatChannelInMetadata_WhenPresent()
    {
        var request = new StripeCheckoutSessionRequest
        {
            PriceId = "price_123",
            Quantity = 1,
            SuccessUrl = "https://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel",
            PlanType = "individual_monthly",
            ManyChatSubscriberId = "sid_123",
            ManyChatChannel = "instagram"
        };

        var options = StripeCheckoutService.BuildSessionCreateOptions(request);

        Assert.Equal("instagram", options.Metadata["manychatChannel"]);
    }
}
