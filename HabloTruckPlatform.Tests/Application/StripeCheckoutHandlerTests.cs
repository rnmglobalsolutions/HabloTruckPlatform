using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeCheckoutHandlerTests
{
    [Theory]
    [InlineData("fleet")]
    [InlineData("fleet_seat")]
    [InlineData("company_seat")]
    [InlineData("fleet_monthly")]
    public async Task ExecuteAsync_Should_MapFleetAliases_ToFleetSeatMonthlyPrice(string planType)
    {
        var checkoutService = new RecordingStripeCheckoutService();
        var sut = new StripeCheckoutHandler(
            checkoutService,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_individual_monthly",
                IndividualYearlyPriceId = "price_individual_yearly",
                FleetSeatMonthlyPriceId = "price_fleet_monthly"
            },
            NullLogger<StripeCheckoutHandler>.Instance);

        var result = await sut.ExecuteAsync(new StripeCheckoutSessionRequest
        {
            PlanType = planType,
            Quantity = 5,
            SuccessUrl = "https://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel",
            CompanyId = "C_001"
        });

        Assert.True(result.Result);
        Assert.Equal("price_fleet_monthly", checkoutService.LastRequest?.PriceId);
        Assert.Equal(planType, checkoutService.LastRequest?.PlanType);
        Assert.False(string.IsNullOrWhiteSpace(result.GeneratedAtUtc));
    }

    [Fact]
    public async Task ExecuteAsync_Should_MapCdlEnglishCohort_ToConfiguredPrice()
    {
        var checkoutService = new RecordingStripeCheckoutService();
        var sut = new StripeCheckoutHandler(
            checkoutService,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_individual_monthly",
                IndividualYearlyPriceId = "price_individual_yearly",
                FleetSeatMonthlyPriceId = "price_fleet_monthly",
                CdlEnglishCohortPriceId = "price_cdl_english"
            },
            NullLogger<StripeCheckoutHandler>.Instance);

        var result = await sut.ExecuteAsync(new StripeCheckoutSessionRequest
        {
            PlanType = "cdl_english_cohort",
            Quantity = 1,
            SuccessUrl = "https://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel",
            Email = "driver@example.com",
            CohortId = "CDL_EN_2026_01"
        });

        Assert.True(result.Result);
        Assert.Equal("price_cdl_english", checkoutService.LastRequest?.PriceId);
        Assert.Equal("cdl_english_cohort", checkoutService.LastRequest?.PlanType);
        Assert.False(string.IsNullOrWhiteSpace(result.GeneratedAtUtc));
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectNonHttpsRedirectUrls()
    {
        var checkoutService = new RecordingStripeCheckoutService();
        var sut = new StripeCheckoutHandler(
            checkoutService,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_individual_monthly",
                IndividualYearlyPriceId = "price_individual_yearly",
                FleetSeatMonthlyPriceId = "price_fleet_monthly"
            },
            NullLogger<StripeCheckoutHandler>.Instance);

        var result = await sut.ExecuteAsync(new StripeCheckoutSessionRequest
        {
            PlanType = "individual_monthly",
            Quantity = 1,
            SuccessUrl = "http://app.hablotruck.com/success",
            CancelUrl = "https://app.hablotruck.com/cancel"
        });

        Assert.False(result.Result);
        Assert.Equal("redirect_url_must_use_https", result.Error);
        Assert.Null(checkoutService.LastRequest);
    }

    [Fact]
    public async Task ExecuteAsync_Should_RejectRedirectHost_WhenNotInAllowList()
    {
        var checkoutService = new RecordingStripeCheckoutService();
        var sut = new StripeCheckoutHandler(
            checkoutService,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_individual_monthly",
                IndividualYearlyPriceId = "price_individual_yearly",
                FleetSeatMonthlyPriceId = "price_fleet_monthly",
                AllowedCheckoutRedirectHosts = ["checkout.hablotruck.com"]
            },
            NullLogger<StripeCheckoutHandler>.Instance);

        var result = await sut.ExecuteAsync(new StripeCheckoutSessionRequest
        {
            PlanType = "individual_monthly",
            Quantity = 1,
            SuccessUrl = "https://evil.example.com/success",
            CancelUrl = "https://checkout.hablotruck.com/cancel"
        });

        Assert.False(result.Result);
        Assert.Equal("redirect_url_host_not_allowed", result.Error);
        Assert.Null(checkoutService.LastRequest);
    }

    private sealed class RecordingStripeCheckoutService : IStripeCheckoutService
    {
        public StripeCheckoutSessionRequest? LastRequest { get; private set; }

        public Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
            StripeCheckoutSessionRequest request,
            CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new StripeCheckoutSessionResult
            {
                Result = true,
                Url = "https://checkout.stripe.test/session",
                SessionId = "cs_test_123"
            });
        }
    }
}
