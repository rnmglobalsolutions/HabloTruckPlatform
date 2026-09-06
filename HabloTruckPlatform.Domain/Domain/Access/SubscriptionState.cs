using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Domain.Stripe;

namespace HabloTruckPlatform.Domain.Access;

public static class SubscriptionState
{
    public static void ApplyStripeStatus(User user, string newStatus, DateTimeOffset nowUtc, GracePolicy gracePolicy)
    {
        user.SubscriptionStatus = newStatus;

        if (StripeStatusMapper.IsActive(newStatus))
        {
            user.IndividualGraceEndsAtUtc = null;
            return;
        }

        if (StripeStatusMapper.IsGraceWorthySubscriptionStatus(newStatus))
        {
            var newGraceEnd = gracePolicy.ComputeGraceEnd(nowUtc);

            if (user.IndividualGraceEndsAtUtc is null || user.IndividualGraceEndsAtUtc < newGraceEnd)
                user.IndividualGraceEndsAtUtc = newGraceEnd;
        }
    }

    public static void ClearGrace(User user)
    {
        user.IndividualGraceEndsAtUtc = null;
    }
}