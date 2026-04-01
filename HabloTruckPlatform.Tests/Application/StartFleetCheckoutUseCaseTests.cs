using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StartFleetCheckoutUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_Should_GenerateCompanyId_AndStartFleetCheckout()
    {
        var checkoutService = new RecordingStripeCheckoutService();
        var stripeCheckoutHandler = new StripeCheckoutHandler(
            checkoutService,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_individual_monthly",
                IndividualYearlyPriceId = "price_individual_yearly",
                FleetSeatMonthlyPriceId = "price_fleet_monthly"
            },
            NullLogger<StripeCheckoutHandler>.Instance);

        var sut = new StartFleetCheckoutUseCase(stripeCheckoutHandler);

        var result = await sut.ExecuteAsync(new StartFleetCheckoutRequest
        {
            CompanyName = "Acme Trucking",
            Email = " owner@acme.com ",
            PhoneE164 = "+15551234567",
            ManyChatSubscriberId = "mc_123",
            Seats = 20,
            SuccessUrl = "https://app.hablotruck.com/company-success",
            CancelUrl = "https://app.hablotruck.com/company-cancel"
        });

        Assert.True(result.Result);
        Assert.StartsWith("C_", result.CompanyId);
        Assert.Equal(20, result.Seats);
        Assert.NotNull(checkoutService.LastRequest);
        Assert.Equal("fleet", checkoutService.LastRequest!.PlanType);
        Assert.Equal(20, checkoutService.LastRequest.Quantity);
        Assert.Equal(20, checkoutService.LastRequest.Seats);
        Assert.Equal(result.CompanyId, checkoutService.LastRequest.CompanyId);
        Assert.Equal("Acme Trucking", checkoutService.LastRequest.CompanyName);
        Assert.Equal("owner@acme.com", checkoutService.LastRequest.Email);
    }

    [Fact]
    public async Task ExecuteAsync_Should_ValidateRequiredFields()
    {
        var checkoutService = new RecordingStripeCheckoutService();
        var stripeCheckoutHandler = new StripeCheckoutHandler(
            checkoutService,
            new StripeOptions
            {
                IndividualMonthlyPriceId = "price_individual_monthly",
                IndividualYearlyPriceId = "price_individual_yearly",
                FleetSeatMonthlyPriceId = "price_fleet_monthly"
            },
            NullLogger<StripeCheckoutHandler>.Instance);

        var sut = new StartFleetCheckoutUseCase(stripeCheckoutHandler);

        var result = await sut.ExecuteAsync(new StartFleetCheckoutRequest
        {
            CompanyName = "Acme Trucking",
            Seats = 0,
            SuccessUrl = "https://app.hablotruck.com/company-success",
            CancelUrl = "https://app.hablotruck.com/company-cancel"
        });

        Assert.False(result.Result);
        Assert.Equal("email_required", result.Error);
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
                SessionId = "cs_test_fleet_123"
            });
        }
    }
}
