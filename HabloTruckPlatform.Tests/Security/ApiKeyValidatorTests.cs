using HabloTruckPlatform.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Domain.Tests.Security;

public sealed class ApiKeyValidatorTests
{
    [Fact]
    public void Validate_Should_ReturnMissing_When_HeaderMissing()
    {
        var sut = BuildValidator("expected-key");

        var result = sut.Validate(null);

        Assert.Equal(ApiKeyValidationResult.Missing, result);
    }

    [Fact]
    public void Validate_Should_ReturnEmpty_When_HeaderValueIsBlank()
    {
        var sut = BuildValidator("expected-key");

        var result = sut.Validate("   ");

        Assert.Equal(ApiKeyValidationResult.Empty, result);
    }

    [Fact]
    public void Validate_Should_ReturnInvalid_When_HeaderValueIsWrong()
    {
        var sut = BuildValidator("expected-key");

        var result = sut.Validate("wrong-key");

        Assert.Equal(ApiKeyValidationResult.Invalid, result);
    }

    [Fact]
    public void Validate_Should_ReturnValid_When_HeaderValueMatches()
    {
        var sut = BuildValidator("expected-key");

        var result = sut.Validate("expected-key");

        Assert.Equal(ApiKeyValidationResult.Valid, result);
    }

    [Fact]
    public void Validate_Should_ReturnNotConfigured_When_ExpectedKeyIsMissing()
    {
        var sut = BuildValidator(string.Empty);

        var result = sut.Validate("any-value");

        Assert.Equal(ApiKeyValidationResult.NotConfigured, result);
    }

    private static ApiKeyValidator BuildValidator(string expectedApiKey)
    {
        var options = Options.Create(new HttpSecurityOptions
        {
            HttpApiKey = expectedApiKey
        });

        return new ApiKeyValidator(options, NullLogger<ApiKeyValidator>.Instance);
    }
}
