using HabloTruckPlatform.Domain.Models;
using Xunit;

namespace HabloTruckPlatform.Domain.Tests.Models;

public class EntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 03, 01, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsActive_LifetimeActiveStatus_ShouldBeActive()
    {
        var ent = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "E1",
            SeatsTotal = 25,
            SeatsUsed = 0,
            Status = "active",
            StartUtc = Now.AddDays(-10),
            EndUtc = null // lifetime
        };

        Assert.True(ent.IsLifetime);
        Assert.True(ent.IsActive(Now));
        Assert.False(ent.IsExpired(Now));
    }

    [Fact]
    public void IsActive_ActiveWithFutureEnd_ShouldBeActive()
    {
        var ent = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "E1",
            SeatsTotal = 25,
            SeatsUsed = 0,
            Status = "active",
            StartUtc = Now.AddDays(-10),
            EndUtc = Now.AddDays(5)
        };

        Assert.False(ent.IsLifetime);
        Assert.True(ent.IsActive(Now));
        Assert.False(ent.IsExpired(Now));
    }

    [Fact]
    public void IsActive_ActiveWithPastEnd_ShouldNotBeActive_AndShouldBeExpired()
    {
        var ent = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "E1",
            SeatsTotal = 25,
            SeatsUsed = 0,
            Status = "active",
            StartUtc = Now.AddDays(-10),
            EndUtc = Now.AddDays(-1)
        };

        Assert.False(ent.IsActive(Now));
        Assert.True(ent.IsExpired(Now));
    }

    [Fact]
    public void IsActive_ExpiredStatus_ShouldNotBeActive()
    {
        var ent = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "E1",
            SeatsTotal = 25,
            SeatsUsed = 0,
            Status = "expired",
            StartUtc = Now.AddDays(-10),
            EndUtc = Now.AddDays(10)
        };

        Assert.False(ent.IsActive(Now));
        Assert.False(ent.IsLifetime);
        Assert.False(ent.IsExpired(Now)); // EndUtc still future, but status says expired
    }

    [Fact]
    public void IsExpired_EndUtcEqualNow_ShouldBeExpired()
    {
        var ent = new Entitlement
        {
            CompanyId = "C1",
            EntitlementId = "E1",
            SeatsTotal = 25,
            SeatsUsed = 0,
            Status = "active",
            StartUtc = Now.AddDays(-10),
            EndUtc = Now
        };

        Assert.True(ent.IsExpired(Now));
        Assert.False(ent.IsActive(Now));
    }
}