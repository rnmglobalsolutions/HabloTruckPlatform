using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage.Mappers;

public static class UserMapper
{
    public static UserEntity ToEntity(User u, string pk)
    {
        return new UserEntity
        {
            PartitionKey = pk,
            RowKey = u.UserId,

            EmailNormalized = u.EmailNormalized,
            ManyChatSubscriberId = u.ManyChatSubscriberId,
            PhoneE164 = u.PhoneE164,

            StripeCustomerId = u.StripeCustomerId,
            StripeSubscriptionId = u.StripeSubscriptionId,
            SubscriptionStatus = u.SubscriptionStatus,
            IndividualGraceEndsAtUtc = u.IndividualGraceEndsAtUtc,

            CompanyId = u.CompanyId,
            SeatEntitlementId = u.SeatEntitlementId,
            SeatStatus = u.SeatStatus,

            EffectiveAccessMode = u.EffectiveAccess?.Mode.ToString(),
            EffectiveAccessSource = u.EffectiveAccess is null ? null : (int)u.EffectiveAccess.Source,
            EffectiveGraceEndsAtUtc = u.EffectiveAccess?.GraceEndsAtUtc,

            LastStripeEventId = u.LastStripeEventId,
            LastStripeEventCreatedUtc = u.LastStripeEventCreatedUtc,
            UpdatedAtUtc = u.UpdatedAtUtc,

            CurrentGracePk = u.CurrentGracePk,
            CurrentGraceRk = u.CurrentGraceRk,

            StripePriceId = u.StripePriceId,
            IndividualPlanTerm = u.IndividualPlanTerm,
            StripeCurrentPeriodEndUtc = u.StripeCurrentPeriodEndUtc,
            StripeCancelAtPeriodEnd = u.StripeCancelAtPeriodEnd,
            PaymentRecoveryStartedAtUtc = u.PaymentRecoveryStartedAtUtc,

            LastSyncedAccessMode = u.LastSyncedAccessMode,
            LastSyncedAccessSource = u.LastSyncedAccessSource,
            LastSyncedGraceEndsAtUtc = u.LastSyncedGraceEndsAtUtc,
            LastManyChatSyncAtUtc = u.LastManyChatSyncAtUtc,

            PlanType = u.PlanType,
            CohortId = u.CohortId,
            SchoolId = u.SchoolId,
            CohortAccessGrantedAtUtc = u.CohortAccessGrantedAtUtc
        };
    }

    public static User FromEntity(UserEntity e)
    {
        AccessSnapshot? snapshot = null;

        if (!string.IsNullOrWhiteSpace(e.EffectiveAccessMode) && e.EffectiveAccessSource is not null)
        {
            if (Enum.TryParse<AccessMode>(e.EffectiveAccessMode, out var mode))
            {
                snapshot = new AccessSnapshot(
                    mode,
                    (AccessSource)e.EffectiveAccessSource.Value,
                    e.EffectiveGraceEndsAtUtc);
            }
        }

        return new User
        {
            UserId = e.RowKey,

            EmailNormalized = e.EmailNormalized,
            ManyChatSubscriberId = e.ManyChatSubscriberId,
            PhoneE164 = e.PhoneE164,

            StripeCustomerId = e.StripeCustomerId,
            StripeSubscriptionId = e.StripeSubscriptionId,
            SubscriptionStatus = e.SubscriptionStatus,
            IndividualGraceEndsAtUtc = e.IndividualGraceEndsAtUtc,

            CompanyId = e.CompanyId,
            SeatEntitlementId = e.SeatEntitlementId,
            SeatStatus = e.SeatStatus,

            EffectiveAccess = snapshot,

            LastStripeEventId = e.LastStripeEventId,
            LastStripeEventCreatedUtc = e.LastStripeEventCreatedUtc,
            UpdatedAtUtc = e.UpdatedAtUtc,

            CurrentGracePk = e.CurrentGracePk,
            CurrentGraceRk = e.CurrentGraceRk,

            StripePriceId = e.StripePriceId,
            IndividualPlanTerm = e.IndividualPlanTerm,
            StripeCurrentPeriodEndUtc = e.StripeCurrentPeriodEndUtc,
            StripeCancelAtPeriodEnd = e.StripeCancelAtPeriodEnd ?? false,
            PaymentRecoveryStartedAtUtc = e.PaymentRecoveryStartedAtUtc,

            LastSyncedAccessMode = e.LastSyncedAccessMode,
            LastSyncedAccessSource = e.LastSyncedAccessSource,
            LastSyncedGraceEndsAtUtc = e.LastSyncedGraceEndsAtUtc,
            LastManyChatSyncAtUtc = e.LastManyChatSyncAtUtc,

            PlanType = e.PlanType,
            CohortId = e.CohortId,
            SchoolId = e.SchoolId,
            CohortAccessGrantedAtUtc = e.CohortAccessGrantedAtUtc
        };
    }
}
