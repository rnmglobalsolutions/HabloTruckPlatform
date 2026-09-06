using HabloTruckPlatform.Domain.Ids;
using Xunit;

namespace HabloTruckPlatform.Domain.Tests.Ids;

public class BucketsTests
{
    [Fact]
    public void StableHashMod_SameInput_ShouldBeDeterministic()
    {
        var v = "01HRF7YQ3ZK6M8N1P2Q4R5S6T7";

        var a = Buckets.StableHashMod(v, 200);
        var b = Buckets.StableHashMod(v, 200);

        Assert.Equal(a, b);
        Assert.InRange(a, 0, 199);
    }

    [Fact]
    public void UserBucketPk_ShouldFormatWithThreeDigits()
    {
        var userId = "01HRF7YQ3ZK6M8N1P2Q4R5S6T7";

        var pk = Buckets.UserBucketPk(userId);

        Assert.StartsWith("HT_U_", pk);
        Assert.Equal(8, pk.Length); // "HT_U_000" => 8
        Assert.Matches(@"^HT_U_\d{3}$", pk);
    }

    [Fact]
    public void ManyChatLookupPk_ShouldFormatWithTwoDigits()
    {
        var subscriberId = "894523741";

        var pk = Buckets.ManyChatLookupPk(subscriberId);

        Assert.StartsWith("HT_MC_", pk);
        Assert.Matches(@"^HT_MC_\d{2}$", pk);
    }

    [Fact]
    public void StripeCustomerLookupPk_ShouldFormatWithTwoDigits()
    {
        var customerId = "cus_Q1abcXYZ";

        var pk = Buckets.StripeCustomerLookupPk(customerId);

        Assert.StartsWith("HT_SC_", pk);
        Assert.Matches(@"^HT_SC_\d{2}$", pk);
    }

    [Theory]
    [InlineData("juan.perez@gmail.com", "HT_EMAIL_ju")]
    [InlineData("a@b.com", "HT_EMAIL_a@")] // first two chars
    [InlineData("x", "HT_EMAIL_x")]        // single-char prefix
    public void EmailLookupPk_ShouldUseFirstTwoCharsOrFallback(string emailNormalized, string expectedPk)
    {
        var pk = Buckets.EmailLookupPk(emailNormalized);
        Assert.Equal(expectedPk, pk);
    }

    [Fact]
    public void StableHashMod_InvalidArgs_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => Buckets.StableHashMod("", 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => Buckets.StableHashMod("abc", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Buckets.StableHashMod("abc", -1));
    }

    [Fact]
    public void UserBucketPk_SameUserId_ShouldAlwaysReturnSamePk()
    {
        var userId = "01HRF8A1BC2D3E4F5G6H7J8K9L";

        var pk1 = Buckets.UserBucketPk(userId);
        var pk2 = Buckets.UserBucketPk(userId);

        Assert.Equal(pk1, pk2);
    }
}
