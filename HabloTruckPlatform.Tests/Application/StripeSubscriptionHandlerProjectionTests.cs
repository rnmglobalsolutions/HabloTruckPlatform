using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class StripeSubscriptionHandlerProjectionTests
{
    [Fact]
    public async Task HandleCheckoutCompletedAsync_Should_InitializeFleetSeatsTotal_FromCheckoutQuantity()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = "evt_checkout_fleet_1",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_fleet_checkout_1",
            SubscriptionId = "sub_fleet_checkout_1",
            CustomerEmail = "admin@fleet.com",
            Quantity = 20,
            PriceId = fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval = "month",
            Metadata = new Dictionary<string, string>
            {
                ["planType"] = "company_seat",
                ["companyId"] = "C_FLEET_1",
                ["companyName"] = "Fleet One"
            }
        });

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_1", "ent_sub_fleet_checkout_1");

        Assert.NotNull(entitlement);
        Assert.Equal(20, entitlement!.SeatsTotal);
        Assert.Equal(0, entitlement.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleCheckoutCompletedAsync_Should_AutoCreateInvite_ForFleetEntitlement()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = "evt_checkout_fleet_invite_1",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_fleet_invite_1",
            SubscriptionId = "sub_fleet_invite_1",
            CustomerEmail = "admin@fleetinvite.com",
            Quantity = 12,
            PriceId = fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval = "month",
            Metadata = new Dictionary<string, string>
            {
                ["planType"] = "company_seat",
                ["companyId"] = "C_FLEET_INVITE_1",
                ["companyName"] = "Fleet Invite One"
            }
        });

        var invites = await fixture.InviteStore.ListForCompanyAsync("C_FLEET_INVITE_1", take: 50);
        var invite = Assert.Single(invites);

        Assert.Equal("C_FLEET_INVITE_1", invite.CompanyId);
        Assert.Equal("ent_sub_fleet_invite_1", invite.EntitlementId);
        Assert.Equal("active", invite.Status);
        Assert.Equal(12, invite.MaxUses);
        Assert.Equal(0, invite.Uses);
        Assert.Equal("system:fleet_checkout_auto", invite.CreatedBy);
        Assert.StartsWith("HT-", invite.Code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleCheckoutCompletedAsync_Should_NotCreateDuplicateInvite_ForRepeatedFleetCheckout()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var checkout = new StripeEventData
        {
            StripeEventId = "evt_checkout_fleet_invite_2",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_fleet_invite_2",
            SubscriptionId = "sub_fleet_invite_2",
            CustomerEmail = "admin2@fleetinvite.com",
            Quantity = 8,
            PriceId = fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval = "month",
            Metadata = new Dictionary<string, string>
            {
                ["planType"] = "company_seat",
                ["companyId"] = "C_FLEET_INVITE_2",
                ["companyName"] = "Fleet Invite Two"
            }
        };

        await fixture.Handler.HandleCheckoutCompletedAsync(checkout);
        await fixture.Handler.HandleCheckoutCompletedAsync(checkout);

        var invites = await fixture.InviteStore.ListForCompanyAsync("C_FLEET_INVITE_2", take: 50);
        var invite = Assert.Single(invites);

        Assert.Equal("ent_sub_fleet_invite_2", invite.EntitlementId);
        Assert.Equal(8, invite.MaxUses);
    }

    [Fact]
    public async Task HandleCheckoutCompletedAsync_Should_GrantDirectAccess_ForCdlEnglishCohortPayment()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = "evt_checkout_cdl_english_1",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_cdl_english_1",
            CustomerEmail = "student@cohort.com",
            CheckoutMode = "payment",
            Quantity = 1,
            PriceId = fixture.PriceCatalog.CdlEnglishCohortPriceId,
            Metadata = new Dictionary<string, string>
            {
                ["planType"] = "cdl_english_cohort",
                ["manychatSubscriberId"] = "sid_cdl_english_1",
                ["ht_cohort"] = "CDL_EN_2026_01"
            }
        });

        var user = fixture.UserStore.GetByEmailNormalized("student@cohort.com");

        Assert.NotNull(user);
        Assert.Equal("cdl_english_cohort", user!.PlanType);
        Assert.Equal("CDL_EN_2026_01", user.CohortId);
        Assert.Equal(now, user.CohortAccessGrantedAtUtc);
        Assert.Equal(AccessMode.Full, user.EffectiveAccess?.Mode);
    }

    [Fact]
    public async Task HandleCheckoutCompletedAsync_Should_BeIdempotent_ForCdlEnglishCohortPayment()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var checkout = new StripeEventData
        {
            StripeEventId = "evt_checkout_cdl_english_2",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_cdl_english_2",
            CustomerEmail = "student2@cohort.com",
            CheckoutMode = "payment",
            Quantity = 1,
            PriceId = fixture.PriceCatalog.CdlEnglishCohortPriceId,
            Metadata = new Dictionary<string, string>
            {
                ["planType"] = "cdl_english_cohort",
                ["manychatSubscriberId"] = "sid_cdl_english_2",
                ["ht_cohort"] = "CDL_EN_2026_02"
            }
        };

        await fixture.Handler.HandleCheckoutCompletedAsync(checkout);
        var first = fixture.UserStore.GetByEmailNormalized("student2@cohort.com");
        var grantedAt = first?.CohortAccessGrantedAtUtc;

        await fixture.Handler.HandleCheckoutCompletedAsync(checkout);
        var second = fixture.UserStore.GetByEmailNormalized("student2@cohort.com");

        Assert.NotNull(second);
        Assert.Equal(grantedAt, second!.CohortAccessGrantedAtUtc);
        Assert.Equal(AccessMode.Full, second.EffectiveAccess?.Mode);
    }

    [Fact]
    public async Task HandleCheckoutCompletedAsync_Should_NotOverwriteRecurringSubscriptionFacts_ForCdlEnglishCohortPayment()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_cdl_english_existing_sub",
            EmailNormalized = "subscriber@cohort.com",
            StripeCustomerId = "cus_existing_subscriber",
            StripeSubscriptionId = "sub_existing_active",
            StripePriceId = fixture.PriceCatalog.IndividualMonthlyPriceId,
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            PlanType = "individual_monthly",
            ManyChatSubscriberId = "sid_existing_subscriber"
        };

        fixture.UserStore.Add(user);

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = "evt_checkout_cdl_english_existing_sub",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_existing_subscriber",
            CustomerEmail = "subscriber@cohort.com",
            CheckoutMode = "payment",
            Quantity = 1,
            PriceId = fixture.PriceCatalog.CdlEnglishCohortPriceId,
            Metadata = new Dictionary<string, string>
            {
                ["planType"] = "cdl_english_cohort",
                ["ht_cohort"] = "CDL_EN_2026_03"
            }
        });

        var updated = fixture.UserStore.GetByEmailNormalized("subscriber@cohort.com");

        Assert.NotNull(updated);
        Assert.Equal("sub_existing_active", updated!.StripeSubscriptionId);
        Assert.Equal(fixture.PriceCatalog.IndividualMonthlyPriceId, updated.StripePriceId);
        Assert.Equal("individual_monthly", updated.PlanType);
        Assert.Equal("monthly", updated.IndividualPlanTerm);
        Assert.Equal("CDL_EN_2026_03", updated.CohortId);
        Assert.Equal(now, updated.CohortAccessGrantedAtUtc);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_UpdateFleetSeatsTotal_WhenQuantityIncreases()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_qty_up",
            StripeCustomerId = "cus_fleet_qty_up",
            StripeSubscriptionId = "sub_fleet_qty_up",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_QTY_UP",
            PlanType = "company_seat",
            ManyChatSubscriberId = "sid_fleet_qty_up"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_qty_up", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_QTY_UP",
            EntitlementId = "ent_sub_fleet_qty_up",
            SeatsTotal = 10,
            SeatsUsed = 4,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_qty_up",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_qty_up",
            StripeSubscriptionId: "sub_fleet_qty_up",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: 15));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_QTY_UP", "ent_sub_fleet_qty_up");

        Assert.NotNull(entitlement);
        Assert.Equal(15, entitlement!.SeatsTotal);
        Assert.Equal(4, entitlement.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_DetectOverCapacity_WhenQuantityDropsBelowUsage()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_qty_down",
            StripeCustomerId = "cus_fleet_qty_down",
            StripeSubscriptionId = "sub_fleet_qty_down",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_QTY_DOWN",
            PlanType = "company_seat",
            ManyChatSubscriberId = "sid_fleet_qty_down"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_qty_down", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_QTY_DOWN",
            EntitlementId = "ent_sub_fleet_qty_down",
            SeatsTotal = 8,
            SeatsUsed = 5,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_qty_down",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_qty_down",
            StripeSubscriptionId: "sub_fleet_qty_down",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: 3));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_QTY_DOWN", "ent_sub_fleet_qty_down");

        Assert.NotNull(entitlement);
        Assert.Equal(3, entitlement!.SeatsTotal);
        Assert.Equal(5, entitlement.SeatsUsed);
        Assert.True(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_SetFleetSeatsTotal_ToZero_WhenStripeQuantityIsZero()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_qty_zero",
            StripeCustomerId = "cus_fleet_qty_zero",
            StripeSubscriptionId = "sub_fleet_qty_zero",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_QTY_ZERO",
            PlanType = "company_seat"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_qty_zero", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_QTY_ZERO",
            EntitlementId = "ent_sub_fleet_qty_zero",
            SeatsTotal = 6,
            SeatsUsed = 2,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_qty_zero",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_qty_zero",
            StripeSubscriptionId: "sub_fleet_qty_zero",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: 0));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_QTY_ZERO", "ent_sub_fleet_qty_zero");

        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement!.SeatsTotal);
        Assert.Equal(2, entitlement.SeatsUsed);
        Assert.True(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_PreserveFleetSeatsTotal_WhenQuantityIsMissing()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_qty_missing",
            StripeCustomerId = "cus_fleet_qty_missing",
            StripeSubscriptionId = "sub_fleet_qty_missing",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_QTY_MISSING",
            PlanType = "company_seat"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_qty_missing", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_QTY_MISSING",
            EntitlementId = "ent_sub_fleet_qty_missing",
            SeatsTotal = 11,
            SeatsUsed = 4,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_qty_missing",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_qty_missing",
            StripeSubscriptionId: "sub_fleet_qty_missing",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: null,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: null));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_QTY_MISSING", "ent_sub_fleet_qty_missing");

        Assert.NotNull(entitlement);
        Assert.Equal(11, entitlement!.SeatsTotal);
        Assert.Equal(4, entitlement.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
        Assert.Equal("active", entitlement.Status);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_BeIdempotent_ForRepeatedFleetQuantityEvent()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_idempotent",
            StripeCustomerId = "cus_fleet_idempotent",
            StripeSubscriptionId = "sub_fleet_idempotent",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_IDEMPOTENT",
            PlanType = "company_seat",
            ManyChatSubscriberId = "sid_fleet_idempotent"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_idempotent", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_IDEMPOTENT",
            EntitlementId = "ent_sub_fleet_idempotent",
            SeatsTotal = 6,
            SeatsUsed = 2,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_idempotent",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_idempotent",
            StripeSubscriptionId: "sub_fleet_idempotent",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: 9));

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_idempotent",
            StripeEventCreatedUtc: now.AddMinutes(1),
            StripeCustomerId: "cus_fleet_idempotent",
            StripeSubscriptionId: "sub_fleet_idempotent",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: 12));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_IDEMPOTENT", "ent_sub_fleet_idempotent");
        var saved = fixture.UserStore.GetById("U_fleet_idempotent");

        Assert.NotNull(entitlement);
        Assert.NotNull(saved);
        Assert.Equal(9, entitlement!.SeatsTotal);
        Assert.Equal("evt_fleet_idempotent", saved!.LastStripeEventId);
    }

    [Fact]
    public async Task HandleInvoicePaymentFailedAsync_Should_ProjectFleetEntitlementPastDue_AndSyncQuantity()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_failed",
            StripeCustomerId = "cus_fleet_failed",
            StripeSubscriptionId = "sub_fleet_failed",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_FAILED",
            PlanType = "company_seat"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_failed", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_FAILED",
            EntitlementId = "ent_sub_fleet_failed",
            SeatsTotal = 8,
            SeatsUsed = 3,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fleet_failed",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_failed",
            StripeSubscriptionId: "sub_fleet_failed",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            Quantity: 5));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_FAILED", "ent_sub_fleet_failed");

        Assert.NotNull(entitlement);
        Assert.Equal("past_due", entitlement!.Status);
        Assert.Equal(5, entitlement.SeatsTotal);
        Assert.Equal(3, entitlement.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleInvoicePaidAsync_Should_ProjectFleetEntitlementActive_AndSyncQuantity()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_paid",
            StripeCustomerId = "cus_fleet_paid",
            StripeSubscriptionId = "sub_fleet_paid",
            SubscriptionStatus = "past_due",
            CompanyId = "C_FLEET_PAID",
            PlanType = "company_seat"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_paid", user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_PAID",
            EntitlementId = "ent_sub_fleet_paid",
            SeatsTotal = 4,
            SeatsUsed = 3,
            IsOverCapacity = false,
            Status = "past_due",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_fleet_paid",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_paid",
            StripeSubscriptionId: "sub_fleet_paid",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: now.AddDays(30),
            Quantity: 6));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_PAID", "ent_sub_fleet_paid");

        Assert.NotNull(entitlement);
        Assert.Equal("active", entitlement!.Status);
        Assert.Equal(6, entitlement.SeatsTotal);
        Assert.Equal(3, entitlement.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_Fallback_ToStripeSnapshot_When_WebhookQuantityIsMissing()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fleet_snapshot",
            StripeCustomerId = "cus_fleet_snapshot",
            StripeSubscriptionId = "sub_fleet_snapshot",
            SubscriptionStatus = "active",
            CompanyId = "C_FLEET_SNAPSHOT",
            PlanType = "company_seat"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fleet_snapshot", user);
        fixture.StripeAdmin.SubscriptionSnapshots["sub_fleet_snapshot"] = new StripeSubscriptionSnapshot(
            SubscriptionId: "sub_fleet_snapshot",
            CustomerId: "cus_fleet_snapshot",
            Status: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            Quantity: 12,
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null);

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_SNAPSHOT",
            EntitlementId = "ent_sub_fleet_snapshot",
            SeatsTotal = 5,
            SeatsUsed = 2,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_fleet_snapshot",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fleet_snapshot",
            StripeSubscriptionId: "sub_fleet_snapshot",
            SubscriptionStatus: "",
            PriceId: null,
            Interval: null,
            CancelAtPeriodEnd: null,
            CurrentPeriodEndUtc: null,
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: null));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_SNAPSHOT", "ent_sub_fleet_snapshot");

        Assert.NotNull(entitlement);
        Assert.Equal(12, entitlement!.SeatsTotal);
        Assert.Equal("active", entitlement.Status);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_ProjectFleetEntitlement_When_UserProjectionIsMissing_ButCompanyExists()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        await fixture.CompanyStore.UpsertAsync(new Company
        {
            CompanyId = "C_FLEET_COMPANY_ONLY",
            StripeCustomerId = "cus_company_only",
            Status = "active",
            CreatedAtUtc = now.AddDays(-10),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FLEET_COMPANY_ONLY",
            EntitlementId = "ent_sub_company_only",
            SeatsTotal = 4,
            SeatsUsed = 1,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = now.AddDays(-5),
            UpdatedAtUtc = now.AddDays(-1)
        });

        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_company_only",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_company_only",
            StripeSubscriptionId: "sub_company_only",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.FleetSeatMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null,
            Quantity: 9));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FLEET_COMPANY_ONLY", "ent_sub_company_only");

        Assert.NotNull(entitlement);
        Assert.Equal(9, entitlement!.SeatsTotal);
        Assert.Equal(1, entitlement.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedAsync_Should_UpdateProjectionFields_ForCancelScheduledSubscription()
    {
        var now = Utc(2026, 3, 10, 12);
        var periodEnd = now.AddDays(14);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_proj_1",
            StripeCustomerId = "cus_proj_1",
            StripeSubscriptionId = "sub_proj_1",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_proj_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_proj_1", user);

        var decision = await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_proj_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_proj_1",
            StripeSubscriptionId: "sub_proj_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: now,
            EndedAtUtc: null));

        var saved = fixture.UserStore.GetById("U_proj_1");

        Assert.NotNull(saved);
        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Full, decision!.Mode);
        Assert.Equal("sub_proj_1", saved!.StripeSubscriptionId);
        Assert.Equal("cus_proj_1", saved.StripeCustomerId);
        Assert.Equal("active", saved.SubscriptionStatus);
        Assert.True(saved.StripeCancelAtPeriodEnd);
        Assert.Equal(periodEnd, saved.StripeCurrentPeriodEndUtc);
        Assert.True(fixture.ManyChatSync.SyncCalls > 0);
    }

    [Fact]
    public async Task HandleInvoicePaymentFailedAsync_Should_OpenGrace_AndTriggerRecoveryFlow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fail_1",
            StripeCustomerId = "cus_fail_1",
            StripeSubscriptionId = "sub_fail_1",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_fail_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fail_1", user);

        var decision = await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fail_1",
            StripeSubscriptionId: "sub_fail_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var saved = fixture.UserStore.GetById("U_fail_1");

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Grace, decision!.Mode);
        Assert.NotNull(saved);
        Assert.Equal("past_due", saved!.SubscriptionStatus);
        Assert.NotNull(saved.IndividualGraceEndsAtUtc);
        Assert.True(saved.IndividualGraceEndsAtUtc > now);
        Assert.Equal(now, saved.PaymentRecoveryStartedAtUtc);
        Assert.Equal(1, fixture.ManyChatSync.PaymentFailedFlowCalls);
        var recoveryUpdate = Assert.Single(fixture.ManyChatSync.BillingRecoveryUpdates);
        Assert.Equal(BillingRecoveryManyChatStatuses.RecoveryActive, recoveryUpdate.Status);
        Assert.True(recoveryUpdate.ActionRequired);
        Assert.Empty(fixture.FailedActions.Enqueued);
    }

    [Fact]
    public async Task HandleInvoicePaymentFailedAsync_Should_TriggerInitialRecoveryFlowOnlyOnce_PerRecoveryEpisode()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fail_once",
            StripeCustomerId = "cus_fail_once",
            StripeSubscriptionId = "sub_fail_once",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_fail_once"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fail_once", user);

        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_once_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fail_once",
            StripeSubscriptionId: "sub_fail_once",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var startedAt = fixture.UserStore.GetById("U_fail_once")!.PaymentRecoveryStartedAtUtc;

        var later = now.AddHours(6);
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_once_2",
            StripeEventCreatedUtc: later,
            StripeCustomerId: "cus_fail_once",
            StripeSubscriptionId: "sub_fail_once",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var saved = fixture.UserStore.GetById("U_fail_once");

        Assert.NotNull(saved);
        Assert.Equal(startedAt, saved!.PaymentRecoveryStartedAtUtc);
        Assert.Equal(1, fixture.ManyChatSync.PaymentFailedFlowCalls);
    }

    [Fact]
    public async Task HandleInvoicePaymentFailedAsync_Should_EnqueueFailedAction_WhenPaymentFlowFailsRetryably()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fail_retry_queue",
            StripeCustomerId = "cus_fail_retry_queue",
            StripeSubscriptionId = "sub_fail_retry_queue",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_fail_retry_queue"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fail_retry_queue", user);
        fixture.ManyChatSync.PaymentFailedFlowException = new ManyChatRequestException(
            path: "fb/sending/sendFlow",
            statusCode: HttpStatusCode.ServiceUnavailable,
            isRetryable: true,
            failureCategory: ManyChatFailureCategory.TransientHttp,
            message: "retryable");

        var decision = await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_retry_queue",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fail_retry_queue",
            StripeSubscriptionId: "sub_fail_retry_queue",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        Assert.NotNull(decision);
        var queued = Assert.Single(fixture.FailedActions.Enqueued);
        Assert.Equal(FailedActionRetryService.ActionManyChatPaymentFailedFlow, queued.ActionType);
    }

    [Fact]
    public async Task HandleInvoicePaymentFailedAsync_Should_NotEnqueueFailedAction_WhenPaymentFlowFailsNonRetryable()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_fail_no_queue",
            StripeCustomerId = "cus_fail_no_queue",
            StripeSubscriptionId = "sub_fail_no_queue",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_fail_no_queue"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_fail_no_queue", user);
        fixture.ManyChatSync.PaymentFailedFlowException = new ManyChatRequestException(
            path: "fb/sending/sendFlow",
            statusCode: HttpStatusCode.BadRequest,
            isRetryable: false,
            failureCategory: ManyChatFailureCategory.PermanentHttp,
            message: "non_retryable");

        var decision = await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_no_queue",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_fail_no_queue",
            StripeSubscriptionId: "sub_fail_no_queue",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        Assert.NotNull(decision);
        Assert.Empty(fixture.FailedActions.Enqueued);
    }

    [Fact]
    public async Task HandleInvoicePaidAsync_Should_ClearGrace_AndRestoreFullAccess()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_recover_1",
            StripeCustomerId = "cus_recover_1",
            StripeSubscriptionId = "sub_recover_1",
            SubscriptionStatus = "past_due",
            IndividualGraceEndsAtUtc = now.AddHours(10),
            PaymentRecoveryStartedAtUtc = now.AddHours(-2),
            ManyChatSubscriberId = "sid_recover_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_recover_1", user);

        var decision = await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_paid_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_recover_1",
            StripeSubscriptionId: "sub_recover_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var saved = fixture.UserStore.GetById("U_recover_1");

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Full, decision!.Mode);
        Assert.NotNull(saved);
        Assert.Equal("active", saved!.SubscriptionStatus);
        Assert.Null(saved.IndividualGraceEndsAtUtc);
        Assert.Null(saved.PaymentRecoveryStartedAtUtc);
        Assert.Equal(0, fixture.ManyChatSync.PaymentFailedFlowCalls);
        var recoveredUpdate = Assert.Single(fixture.ManyChatSync.BillingRecoveryUpdates);
        Assert.Equal(BillingRecoveryManyChatStatuses.Recovered, recoveredUpdate.Status);
        Assert.True(recoveredUpdate.Recovered);
    }

    [Fact]
    public async Task HandleSubscriptionDeletedAsync_Should_BlockAndClearGrace_When_EndedNow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_deleted_1",
            StripeCustomerId = "cus_deleted_1",
            StripeSubscriptionId = "sub_deleted_1",
            SubscriptionStatus = "active",
            IndividualGraceEndsAtUtc = now.AddHours(8),
            ManyChatSubscriberId = "sid_deleted_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_deleted_1", user);

        var decision = await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_deleted_1",
            StripeEventCreatedUtc: now,
            StripeCustomerId: "cus_deleted_1",
            StripeSubscriptionId: "sub_deleted_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now,
            CanceledAtUtc: now,
            EndedAtUtc: now));

        var saved = fixture.UserStore.GetById("U_deleted_1");

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Blocked, decision!.Mode);
        Assert.NotNull(saved);
        Assert.Equal("deleted", saved!.SubscriptionStatus);
        Assert.Null(saved.IndividualGraceEndsAtUtc);
    }

    [Fact]
    public async Task HandleCustomerUpdatedAsync_Should_RetryOpenInvoice_When_RecoveryIsActive()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);
        fixture.StripeAdmin.RetryAttempt = new StripeOpenInvoiceRetryAttempt(
            CustomerId: "cus_customer_update_1",
            SubscriptionId: "sub_customer_update_1",
            InvoiceId: "in_customer_update_1",
            InvoiceStatus: "paid",
            CollectionMethod: "charge_automatically",
            InvoiceFound: true,
            PaymentAttempted: true,
            InvoicePaid: true);

        var user = new User
        {
            UserId = "U_customer_update_1",
            StripeCustomerId = "cus_customer_update_1",
            StripeSubscriptionId = "sub_customer_update_1",
            SubscriptionStatus = "past_due",
            PaymentRecoveryStartedAtUtc = now.AddHours(-3),
            ManyChatSubscriberId = "sid_customer_update_1"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_customer_update_1", user);

        await fixture.Handler.HandleCustomerUpdatedAsync(new StripeEventData
        {
            StripeEventId = "evt_customer_update_1",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_customer_update_1",
            PaymentMethodUpdated = true
        });

        Assert.Equal("cus_customer_update_1", fixture.StripeAdmin.LastRetryCustomerId);
        Assert.Equal("sub_customer_update_1", fixture.StripeAdmin.LastRetrySubscriptionId);
        var update = Assert.Single(fixture.ManyChatSync.BillingRecoveryUpdates);
        Assert.Equal(BillingRecoveryManyChatStatuses.PaymentUpdatePendingConfirmation, update.Status);
    }

    [Fact]
    public async Task HandleCustomerUpdatedAsync_Should_NotRetry_When_RecoveryIsNotActive()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_customer_update_2",
            StripeCustomerId = "cus_customer_update_2",
            StripeSubscriptionId = "sub_customer_update_2",
            SubscriptionStatus = "active",
            ManyChatSubscriberId = "sid_customer_update_2"
        };

        fixture.UserStore.Add(user);
        fixture.UserResolver.Map("cus_customer_update_2", user);

        await fixture.Handler.HandleCustomerUpdatedAsync(new StripeEventData
        {
            StripeEventId = "evt_customer_update_2",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_customer_update_2",
            PaymentMethodUpdated = true
        });

        Assert.Null(fixture.StripeAdmin.LastRetryCustomerId);
        Assert.Empty(fixture.ManyChatSync.BillingRecoveryUpdates);
    }

    private static HandlerFixture BuildFixture(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        var userStore = new InMemoryUserStore();
        var entitlementStore = new InMemoryEntitlementStore();
        var userResolver = new InMemoryUserResolver();
        var graceIndexStore = new InMemoryGraceIndexStore();
        var manyChat = new RecordingManyChatSync();
        var failedActions = new NoopFailedActionStore();
        var seatStore = new NoopSeatAssignmentStore();
        var companyStore = new InMemoryCompanyStore();
        var inviteStore = new InMemoryInviteCodeStore();
        var expiryIndexStore = new InMemoryEntitlementExpiryIndexStore();
        var stripeAdmin = new NoopStripeAdminClient();
        var billingRecoveryNotifier = new BillingRecoveryManyChatNotifier(
            manyChat,
            failedActions,
            clock);

        var orchestrator = new AccessOrchestrator(
            userStore,
            seatStore,
            entitlementStore,
            manyChat,
            failedActions,
            clock,
            new CompanyGracePolicy(7));

        var priceCatalog = new global::HabloTruckPlatform.Application.Integrations.Stripex.StripeOptions
        {
            WebhookSecret = "whsec_test",
            StripeSecretKey = "sk_test",
            IndividualMonthlyPriceId = "price_ind_monthly",
            IndividualYearlyPriceId = "price_ind_yearly",
            FleetSeatMonthlyPriceId = "price_fleet_monthly",
            CdlEnglishCohortPriceId = "price_cdl_english"
        };
        var companyAdminInviteService = new CompanyAdminInviteService(
            companyStore,
            entitlementStore,
            inviteStore,
            NullLogger<CompanyAdminInviteService>.Instance);

        var handler = new StripeSubscriptionHandler(
            userResolver,
            userStore,
            graceIndexStore,
            manyChat,
            orchestrator,
            clock,
            new GracePolicy(72),
            priceCatalog,
            companyStore,
            entitlementStore,
            companyAdminInviteService,
            expiryIndexStore,
            failedActions,
            stripeAdmin,
            billingRecoveryNotifier,
            NullLogger<StripeSubscriptionHandler>.Instance);

        return new HandlerFixture(handler, userStore, userResolver, manyChat, priceCatalog, failedActions, stripeAdmin, entitlementStore, companyStore, inviteStore);
    }

    private sealed record HandlerFixture(
        StripeSubscriptionHandler Handler,
        InMemoryUserStore UserStore,
        InMemoryUserResolver UserResolver,
        RecordingManyChatSync ManyChatSync,
        global::HabloTruckPlatform.Application.Integrations.Stripex.StripeOptions PriceCatalog,
        NoopFailedActionStore FailedActions,
        NoopStripeAdminClient StripeAdmin,
        InMemoryEntitlementStore EntitlementStore,
        InMemoryCompanyStore CompanyStore,
        InMemoryInviteCodeStore InviteStore);

    private static DateTimeOffset Utc(int y, int m, int d, int h)
        => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserResolver : IUserResolver
    {
        private readonly Dictionary<string, UserRef> _byStripe = new(StringComparer.OrdinalIgnoreCase);

        public void Map(string stripeCustomerId, User user)
            => _byStripe[stripeCustomerId] = new UserRef(Buckets.UserBucketPk(user.UserId), user.UserId);

        public Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult(_byStripe.TryGetValue(stripeCustomerId, out var userRef) ? (UserRef?)userRef : null);

        public Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);

        public Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
            => Task.FromResult<UserRef?>(null);
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _users = new();

        public void Add(User user)
            => _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public User? GetById(string userId)
        {
            _users.TryGetValue((Buckets.UserBucketPk(userId), userId), out var user);
            return user;
        }

        public User? GetByEmailNormalized(string emailNormalized)
            => _users.Values.FirstOrDefault(user =>
                string.Equals(user.EmailNormalized, emailNormalized, StringComparison.OrdinalIgnoreCase));

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(_users.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
        {
            var existing = _users.Values.FirstOrDefault(user =>
                (!string.IsNullOrWhiteSpace(manyChatSubscriberId) && string.Equals(user.ManyChatSubscriberId, manyChatSubscriberId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(emailNormalized) && string.Equals(user.EmailNormalized, emailNormalized, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(phoneE164) && string.Equals(user.PhoneE164, phoneE164, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
                return Task.FromResult(existing);

            var user = new User
            {
                UserId = UlidIds.NewUserId(),
                EmailNormalized = emailNormalized,
                ManyChatSubscriberId = manyChatSubscriberId,
                PhoneE164 = phoneE164
            };

            _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.FromResult(user);
        }

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());
    }

    private sealed class InMemoryGraceIndexStore : IGraceIndexStore
    {
        public Task UpsertAsync(UserRef userRef, DateTimeOffset graceEndsAtUtc, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteForUserAsync(UserRef userRef, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<GraceIndexItem>> QueryExpiredAsync(string gracePk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GraceIndexItem>>(Array.Empty<GraceIndexItem>());

        public Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public int SyncCalls { get; private set; }
        public int PaymentFailedFlowCalls { get; private set; }
        public List<BillingRecoveryManyChatUpdate> BillingRecoveryUpdates { get; } = new();
        public Exception? PaymentFailedFlowException { get; set; }

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
        {
            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
        {
            PaymentFailedFlowCalls++;

            if (PaymentFailedFlowException is not null)
                throw PaymentFailedFlowException;

            return Task.CompletedTask;
        }

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SyncBillingRecoveryStatusAsync(BillingRecoveryManyChatUpdate update, CancellationToken ct = default)
        {
            BillingRecoveryUpdates.Add(update);
            return Task.CompletedTask;
        }

        public Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
        {
            return Task.FromResult(new ManyChatResponse
            {
                status = "ok",
                message = $"Removed tag {tagName} from subscriber {subscriberId}"
            });
        }

        public Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default)
        {
            return Task.FromResult(new ManyChatResponse
            {
                status = "ok",
                message = $"Added tag {tagName} to subscriber {subscriberId}"
            });
        }

        public Task<ManyChatResponse> SetCustomFieldByNameAsync(string subscriberId, string fieldName, string value, CancellationToken ct = default)
        {
            return Task.FromResult(new ManyChatResponse
            {
                status = "ok",
                message = $"Set custom field {fieldName} to {value} for subscriber {subscriberId}"
            });
        }
    }

    private sealed class NoopSeatAssignmentStore : ISeatAssignmentStore
    {
        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult<SeatAssignment?>(null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class InMemoryCompanyStore : ICompanyStore
    {
        private readonly Dictionary<string, Company> _companies = new(StringComparer.OrdinalIgnoreCase);

        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
            => Task.FromResult(_companies.TryGetValue(companyId, out var company) ? company : null);

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult(_companies.Values.FirstOrDefault(c => string.Equals(c.StripeCustomerId, stripeCustomerId, StringComparison.OrdinalIgnoreCase)));

        public Task UpsertAsync(Company company, CancellationToken ct = default)
        {
            _companies[company.CompanyId] = company;
            return Task.CompletedTask;
        }

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
        {
            _companies[companyId] = new Company
            {
                CompanyId = companyId,
                Name = companyName,
                AdminEmailNormalized = adminEmailNormalized,
                StripeCustomerId = stripeCustomerId,
                Status = "active"
            };
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryInviteCodeStore : IInviteCodeStore
    {
        private readonly Dictionary<string, InviteCode> _invites = new(StringComparer.OrdinalIgnoreCase);

        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<InviteCode?> GetAsync(string code, CancellationToken ct = default)
            => Task.FromResult(_invites.TryGetValue(code, out var invite) ? invite : null);

        public Task CreateAsync(InviteCode invite, CancellationToken ct = default)
        {
            if (_invites.ContainsKey(invite.Code))
                throw new InvalidOperationException($"Invite code already exists: {invite.Code}");

            _invites[invite.Code] = invite;
            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeAsync(string code, DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task UpsertAsync(InviteCode invite, CancellationToken ct = default)
        {
            _invites[invite.Code] = invite;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InviteCode>> ListForCompanyAsync(string companyId, int take = 100, CancellationToken ct = default)
        {
            var invites = _invites.Values
                .Where(invite => string.Equals(invite.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<InviteCode>>(invites);
        }
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _rows = new();

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, entitlementId), out var entitlement) ? entitlement : null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((companyId, entitlementId), out var entitlement))
            {
                entitlement.Status = status;
                _rows[(companyId, entitlementId)] = entitlement;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryEntitlementExpiryIndexStore : IEntitlementExpiryIndexStore
    {
        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(EntitlementRef entitlementRef, DateTimeOffset endUtc, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<EntitlementExpiryIndexItem>> QueryExpiringAsync(string expiryPk, DateTimeOffset nowUtc, int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EntitlementExpiryIndexItem>>(Array.Empty<EntitlementExpiryIndexItem>());

        public Task DeleteAsync(string pk, string rk, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopFailedActionStore : IFailedActionStore
    {
        public List<FailedActionItem> Enqueued { get; } = new();

        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
        {
            Enqueued.Add(new FailedActionItem("pk", "rk", actionType, payloadJson, 0, nextRetryUtc));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FailedActionItem>> GetDueAsync(DateTimeOffset nowUtc, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());

        public Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RescheduleAsync(string pk, string rk, int attempts, DateTimeOffset nextRetryUtc, string lastError, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task MarkDeadAsync(string pk, string rk, int attempts, string lastError, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(DateTimeOffset nowUtc, string status, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());

        public Task RequeueAsync(string pk, string rk, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopStripeAdminClient : IStripeAdminClient
    {
        public Dictionary<string, StripeSubscriptionSnapshot> SubscriptionSnapshots { get; } = new(StringComparer.OrdinalIgnoreCase);
        public StripeOpenInvoiceRetryAttempt RetryAttempt { get; set; } = new(
            CustomerId: "cus_default",
            SubscriptionId: "sub_default",
            InvoiceId: null,
            InvoiceStatus: null,
            CollectionMethod: null,
            InvoiceFound: false,
            PaymentAttempted: false,
            InvoicePaid: false);

        public string? LastRetryCustomerId { get; private set; }
        public string? LastRetrySubscriptionId { get; private set; }

        public Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(SubscriptionSnapshots.TryGetValue(subscriptionId, out var snapshot) ? snapshot : null);

        public Task<StripeEventData?> GetEventDataAsync(string eventId, CancellationToken ct = default)
            => Task.FromResult<StripeEventData?>(null);

        public Task<StripePaymentMethodUpdateSession> CreatePaymentMethodUpdateSessionAsync(string customerId, string? subscriptionId, string returnUrl, CancellationToken ct = default)
            => Task.FromResult(new StripePaymentMethodUpdateSession("bps_default", customerId, subscriptionId, returnUrl));

        public Task<StripeOpenInvoiceRetryAttempt> RetryOpenInvoiceAsync(string customerId, string subscriptionId, CancellationToken ct = default)
        {
            LastRetryCustomerId = customerId;
            LastRetrySubscriptionId = subscriptionId;
            return Task.FromResult(RetryAttempt);
        }
    }
}
