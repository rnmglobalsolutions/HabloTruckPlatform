using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Domain.Access;

public static class AccessRules
{
    public static AccessDecision Decide(
        UserAccessContext ctx,
        DateTimeOffset nowUtc,
        CompanyGracePolicy companyGracePolicy)
    {
        // Company access states
        bool seatActive = ctx.Seat is not null && ctx.Seat.IsActive();

        bool companyFull =
            seatActive
            && ctx.Entitlement is not null
            && ctx.Entitlement.IsActive(nowUtc);

        bool companyGrace =
            seatActive
            && ctx.Entitlement is not null
            && !ctx.Entitlement.IsLifetime
            && ctx.Entitlement.EndUtc is not null
            && ctx.Entitlement.IsExpired(nowUtc)
            && companyGracePolicy.IsInGrace(nowUtc, ctx.Entitlement.EndUtc.Value);

        // Individual states
        bool individualFull = string.Equals(ctx.User.SubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase);
        bool individualGrace = ctx.User.IndividualGraceEndsAtUtc is not null && ctx.User.IndividualGraceEndsAtUtc > nowUtc;

        // Decision priority
        if (companyFull && individualFull)
            return new AccessDecision(AccessMode.Full, AccessSource.Both, null, "Individual active + company ctx.Entitlement active");

        if (companyFull)
            return new AccessDecision(AccessMode.Full, AccessSource.Company, null, "Company ctx.Entitlement active");

        if (individualFull)
            return new AccessDecision(AccessMode.Full, AccessSource.Individual, null, "Individual subscription active");

        // Grace (either source)
        if (individualGrace && companyGrace)
        {
            // choose the later grace end date as the displayed one (optional)
            var companyGraceEndsAt = companyGracePolicy.ComputeGraceEnd(ctx.Entitlement!.EndUtc!.Value);
            var graceEndsAt = Max(ctx.User.IndividualGraceEndsAtUtc!.Value, companyGraceEndsAt);

            return new AccessDecision(AccessMode.Grace, AccessSource.Both, graceEndsAt, "Grace active (individual + company)");
        }

        if (companyGrace)
        {
            var companyGraceEndsAt = companyGracePolicy.ComputeGraceEnd(ctx.Entitlement!.EndUtc!.Value);
            return new AccessDecision(AccessMode.Grace, AccessSource.Company, companyGraceEndsAt, "Company grace (7 days)");
        }

        if (individualGrace)
            return new AccessDecision(AccessMode.Grace, AccessSource.Individual, ctx.User.IndividualGraceEndsAtUtc, "Individual grace active");

        return new AccessDecision(AccessMode.Blocked, AccessSource.None, null, "No active subscription/ctx.Entitlement");
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;
}