using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;
using System.Security.AccessControl;
using Xunit;

public class AccessRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 03, 01, 0, 0, 0, TimeSpan.Zero);
    private static readonly CompanyGracePolicy CompanyGrace = new(7);

    private static User NewUser() => new()
    {
        UserId = "U1",
        SubscriptionStatus = null,
        IndividualGraceEndsAtUtc = null,
        CompanyId = null,
        SeatStatus = null
    };

    private static SeatAssignment ActiveSeat(string companyId, string entitlementId) => new()
    {
        CompanyId = companyId,
        UserId = "U1",
        EntitlementId = entitlementId,
        Status = "active",
        AssignedAtUtc = Now
    };

    private static Entitlement ActiveEntitlement(string companyId, string entitlementId) => new()
    {
        CompanyId = companyId,
        EntitlementId = entitlementId,
        SeatsTotal = 10,
        SeatsUsed = 1,
        Status = "active",
        StartUtc = Now.AddDays(-1),
        EndUtc = Now.AddDays(30)
    };

    private static Entitlement ExpiredEntitlement(string companyId, string entitlementId, int daysAgo) => new()
    {
        CompanyId = companyId,
        EntitlementId = entitlementId,
        SeatsTotal = 10,
        SeatsUsed = 1,
        Status = "active",
        StartUtc = Now.AddDays(-30),
        EndUtc = Now.AddDays(-daysAgo)
    };

    // ---------------------------
    // Individual tests
    // ---------------------------

    [Fact]
    public void IndividualActive_ShouldBeFull()
    {
        var user = NewUser();
        user.SubscriptionStatus = "active";

        var ctx = new UserAccessContext(user, null, null);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Full, decision.Mode);
        Assert.Equal(AccessSource.Individual, decision.Source);
    }

    [Fact]
    public void IndividualGrace_ShouldBeGrace()
    {
        var user = NewUser();
        user.IndividualGraceEndsAtUtc = Now.AddHours(10);

        var ctx = new UserAccessContext(user, null, null);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Grace, decision.Mode);
        Assert.Equal(AccessSource.Individual, decision.Source);
    }

    // ---------------------------
    // Company full
    // ---------------------------

    [Fact]
    public void CompanyActiveSeatAndEntitlement_ShouldBeFull()
    {
        var user = NewUser();
        user.CompanyId = "C1";
        user.SeatStatus = "active";

        var seat = ActiveSeat("C1", "E1");
        var ent = ActiveEntitlement("C1", "E1");

        var ctx = new UserAccessContext(user, seat, ent);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Full, decision.Mode);
        Assert.Equal(AccessSource.Company, decision.Source);
    }

    // ---------------------------
    // Company grace (within 7 days)
    // ---------------------------

    [Fact]
    public void CompanyExpiredWithinGrace_ShouldBeGrace()
    {
        var user = NewUser();
        user.CompanyId = "C1";
        user.SeatStatus = "active";

        var seat = ActiveSeat("C1", "E1");
        var ent = ExpiredEntitlement("C1", "E1", daysAgo: 3); // expired 3 days ago → still in 7-day grace

        var ctx = new UserAccessContext(user, seat, ent);

        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Grace, decision.Mode);
        Assert.Equal(AccessSource.Company, decision.Source);
        Assert.NotNull(decision.GraceEndsAtUtc);
    }

    [Fact]
    public void CompanyExpiredAfterGrace_ShouldBeBlocked()
    {
        var user = NewUser();
        user.CompanyId = "C1";
        user.SeatStatus = "active";

        var seat = ActiveSeat("C1", "E1");
        var ent = ExpiredEntitlement("C1", "E1", daysAgo: 10); // beyond 7-day grace

        var ctx = new UserAccessContext(user, seat, ent);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Blocked, decision.Mode);
    }

    // ---------------------------
    // Priority rules
    // ---------------------------

    [Fact]
    public void IndividualActive_ShouldOverrideCompanyExpired()
    {
        var user = NewUser();
        user.SubscriptionStatus = "active";
        user.CompanyId = "C1";
        user.SeatStatus = "active";

        var seat = ActiveSeat("C1", "E1");
        var ent = ExpiredEntitlement("C1", "E1", daysAgo: 10);

        var ctx = new UserAccessContext(user, seat, ent);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Full, decision.Mode);
        Assert.Equal(AccessSource.Individual, decision.Source);
    }

    [Fact]
    public void IndividualGraceAndCompanyGrace_ShouldBeGraceWithBothSource()
    {
        var user = NewUser();
        user.IndividualGraceEndsAtUtc = Now.AddHours(5);
        user.CompanyId = "C1";
        user.SeatStatus = "active";

        var seat = ActiveSeat("C1", "E1");
        var ent = ExpiredEntitlement("C1", "E1", daysAgo: 3);

        var ctx = new UserAccessContext(user, seat, ent);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Grace, decision.Mode);
        Assert.Equal(AccessSource.Both, decision.Source);
        Assert.NotNull(decision.GraceEndsAtUtc);
    }

    [Fact]
    public void NoSubscriptionNoSeat_ShouldBeBlocked()
    {
        var user = NewUser();

        var ctx = new UserAccessContext(user, null, null);
        var decision = AccessRules.Decide(ctx, Now, CompanyGrace);

        Assert.Equal(AccessMode.Blocked, decision.Mode);
    }
}