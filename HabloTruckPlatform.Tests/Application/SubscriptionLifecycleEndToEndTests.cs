using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.Stripex;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class SubscriptionLifecycleEndToEndTests
{
    [Fact]
    public async Task EndToEnd_NewSubscriptionPurchase_Should_EndWithActiveUserAndFullAccess()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = "evt_checkout_1",
            StripeEventCreatedUtc = now,
            CustomerId = "cus_e2e_1",
            SubscriptionId = "sub_e2e_1",
            CustomerEmail = "driver1@hablotruck.com",
            PriceId = fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval = "month",
            Metadata = new Dictionary<string, string>
            {
                ["manychatSubscriberId"] = "sid_e2e_1",
                ["planType"] = "individual_monthly"
            }
        });

        fixture.Clock.UtcNow = now.AddMinutes(1);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_sub_updated_1",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_e2e_1",
            StripeSubscriptionId: "sub_e2e_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = now.AddMinutes(2);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_invoice_paid_1",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_e2e_1",
            StripeSubscriptionId: "sub_e2e_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var user = fixture.UserStore.GetByStripeCustomer("cus_e2e_1");

        Assert.NotNull(user);
        Assert.Equal("sub_e2e_1", user!.StripeSubscriptionId);
        Assert.Equal("active", user.SubscriptionStatus);
        Assert.NotNull(user.EffectiveAccess);
        Assert.Equal(AccessMode.Full, user.EffectiveAccess!.Mode);
        Assert.Equal("monthly", user.IndividualPlanTerm);
        Assert.Equal("individual_monthly", user.PlanType);
        Assert.Equal(now.AddDays(30), user.StripeCurrentPeriodEndUtc);
        Assert.Null(user.IndividualGraceEndsAtUtc);
    }

    [Fact]
    public async Task EndToEnd_PaymentFailureThenRecovery_Should_EnterGraceThenRestoreFullAccess()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_e2e_fail", "sub_e2e_fail", "driver2@hablotruck.com", "sid_e2e_fail");

        fixture.Clock.UtcNow = now.AddDays(31);
        var failedDecision = await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_fail_1",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_e2e_fail",
            StripeSubscriptionId: "sub_e2e_fail",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        Assert.NotNull(failedDecision);
        Assert.Equal(AccessMode.Grace, failedDecision!.Mode);

        var afterFailure = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("past_due", afterFailure.SubscriptionStatus);
        Assert.NotNull(afterFailure.IndividualGraceEndsAtUtc);

        fixture.Clock.UtcNow = now.AddDays(31).AddHours(1);
        var paidDecision = await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_paid_2",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_e2e_fail",
            StripeSubscriptionId: "sub_e2e_fail",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        Assert.NotNull(paidDecision);
        Assert.Equal(AccessMode.Full, paidDecision!.Mode);

        var afterRecovery = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("active", afterRecovery.SubscriptionStatus);
        Assert.Null(afterRecovery.IndividualGraceEndsAtUtc);
        Assert.Equal(1, fixture.ManyChat.PaymentFailedFlowCalls);
    }

    [Fact]
    public async Task EndToEnd_CancelAtPeriodEnd_Should_KeepAccessUntilPeriodEnd_ThenRevokeOnDeleted()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_e2e_cancel", "sub_e2e_cancel", "driver3@hablotruck.com", "sid_e2e_cancel");

        var periodEnd = now.AddDays(5);
        fixture.Clock.UtcNow = now.AddDays(1);

        var scheduledDecision = await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_cancel_schedule",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_e2e_cancel",
            StripeSubscriptionId: "sub_e2e_cancel",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: fixture.Clock.UtcNow,
            EndedAtUtc: null));

        Assert.NotNull(scheduledDecision);
        Assert.Equal(AccessMode.Full, scheduledDecision!.Mode);

        var scheduledUser = fixture.UserStore.Get(user.UserId)!;
        Assert.True(scheduledUser.StripeCancelAtPeriodEnd);
        Assert.Equal(periodEnd, scheduledUser.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Full, scheduledUser.EffectiveAccess!.Mode);

        fixture.Clock.UtcNow = periodEnd.AddDays(1);
        var deletedDecision = await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: "evt_cancel_deleted",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_e2e_cancel",
            StripeSubscriptionId: "sub_e2e_cancel",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: periodEnd,
            EndedAtUtc: fixture.Clock.UtcNow));

        Assert.NotNull(deletedDecision);
        Assert.Equal(AccessMode.Blocked, deletedDecision!.Mode);

        var endedUser = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("deleted", endedUser.SubscriptionStatus);
        Assert.Equal(AccessMode.Blocked, endedUser.EffectiveAccess!.Mode);
    }

    [Fact]
    public async Task EndToEnd_OutOfOrderSequence_Should_IgnoreOlderUpdatedEvent_AndKeepNewestState()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_order_1", "sub_order_1", "driver-order@hablotruck.com", "sid_order_1");

        var t1 = now.AddMinutes(10);
        var t2 = now.AddMinutes(20);
        var t0 = now.AddMinutes(5);

        fixture.Clock.UtcNow = t1;
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_order_a",
            StripeEventCreatedUtc: t1,
            StripeCustomerId: "cus_order_1",
            StripeSubscriptionId: "sub_order_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = t2;
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_order_b",
            StripeEventCreatedUtc: t2,
            StripeCustomerId: "cus_order_1",
            StripeSubscriptionId: "sub_order_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: now.AddDays(60)));

        fixture.Clock.UtcNow = t2.AddMinutes(1);
        var outOfOrder = await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_order_c_old",
            StripeEventCreatedUtc: t0,
            StripeCustomerId: "cus_order_1",
            StripeSubscriptionId: "sub_order_1",
            SubscriptionStatus: "past_due",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(5),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        Assert.NotNull(outOfOrder);
        Assert.Contains("out-of-order", outOfOrder!.Reason, StringComparison.OrdinalIgnoreCase);

        var saved = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal(t2, saved.LastStripeEventCreatedUtc);
        Assert.Equal("active", saved.SubscriptionStatus);
        Assert.Equal(now.AddDays(60), saved.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Full, saved.EffectiveAccess!.Mode);
    }

    [Fact]
    public async Task EndToEnd_RenewalInvoicePaid_Should_ExtendCurrentPeriodEnd_AndKeepAccessActive()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_renew_1", "sub_renew_1", "driver-renew@hablotruck.com", "sid_renew_1");
        var before = fixture.UserStore.Get(user.UserId)!;
        var previousPeriodEnd = Assert.IsType<DateTimeOffset>(before.StripeCurrentPeriodEndUtc);
        var userCountBefore = fixture.UserStore.Count;

        var renewedPeriodEnd = previousPeriodEnd.AddDays(30);

        fixture.Clock.UtcNow = now.AddDays(29);
        var decision = await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_renew_1",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_renew_1",
            StripeSubscriptionId: "sub_renew_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: renewedPeriodEnd));

        Assert.NotNull(decision);
        Assert.Equal(AccessMode.Full, decision!.Mode);

        var after = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("sub_renew_1", after.StripeSubscriptionId);
        Assert.True(after.StripeCurrentPeriodEndUtc > previousPeriodEnd);
        Assert.Equal(renewedPeriodEnd, after.StripeCurrentPeriodEndUtc);
        Assert.Equal(AccessMode.Full, after.EffectiveAccess!.Mode);
        Assert.Equal(userCountBefore, fixture.UserStore.Count);
    }
    [Fact]
    public async Task EndToEnd_RenewalReminderFlow_Should_DispatchDueReminder()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = new User
        {
            UserId = "U_e2e_reminder",
            ManyChatSubscriberId = "sid_e2e_reminder",
            StripeCustomerId = "cus_e2e_reminder",
            StripeSubscriptionId = "sub_e2e_reminder",
            SubscriptionStatus = "active",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(1),
            IndividualPlanTerm = "monthly",
            PlanType = "individual_monthly"
        };

        fixture.UserStore.Add(user);

        await fixture.ReminderService.RunDailyAsync(100);
        await fixture.ReminderService.RunDailyAsync(100);

        var dispatch = Assert.Single(fixture.ManyChat.Reminders);
        Assert.Equal("renewal_reminder_1d", dispatch.ReminderType);
        Assert.True(dispatch.UsePositiveContinuityFraming);
        Assert.Equal("PositiveContinuity", dispatch.ReminderTone);
    }

    [Fact]
    public async Task ReplaySafety_Should_IgnoreOlderEvents_AndKeepStableFinalState_AfterBatchReplay()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_replay_1", "sub_replay_1", "driver4@hablotruck.com", "sid_replay_1");

        var t1 = now.AddMinutes(10);
        var t2 = now.AddMinutes(20);
        var t3 = now.AddMinutes(30);

        fixture.Clock.UtcNow = t1;
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_replay_sub_1",
            StripeEventCreatedUtc: t1,
            StripeCustomerId: "cus_replay_1",
            StripeSubscriptionId: "sub_replay_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(28),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = t2;
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_replay_fail_1",
            StripeEventCreatedUtc: t2,
            StripeCustomerId: "cus_replay_1",
            StripeSubscriptionId: "sub_replay_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        fixture.Clock.UtcNow = t3;
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_replay_paid_1",
            StripeEventCreatedUtc: t3,
            StripeCustomerId: "cus_replay_1",
            StripeSubscriptionId: "sub_replay_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        fixture.Clock.UtcNow = t3.AddMinutes(1);
        var outOfOrder = await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_replay_old_fail",
            StripeEventCreatedUtc: t2.AddMinutes(-1),
            StripeCustomerId: "cus_replay_1",
            StripeSubscriptionId: "sub_replay_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        Assert.NotNull(outOfOrder);
        Assert.Contains("out-of-order", outOfOrder!.Reason, StringComparison.OrdinalIgnoreCase);

        fixture.Clock.UtcNow = t3.AddMinutes(2);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_replay_paid_1_dup",
            StripeEventCreatedUtc: t3,
            StripeCustomerId: "cus_replay_1",
            StripeSubscriptionId: "sub_replay_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var finalUser = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("active", finalUser.SubscriptionStatus);
        Assert.Null(finalUser.IndividualGraceEndsAtUtc);
        Assert.Equal(AccessMode.Full, finalUser.EffectiveAccess!.Mode);
    }


    [Fact]
    public async Task EndToEnd_PaymentFailureWithDuplicateRetry_Should_KeepGraceStable_UntilRecovery()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_dup_retry_1", "sub_dup_retry_1", "driver-dup-retry@hablotruck.com", "sid_dup_retry_1");

        var failCreated = now.AddDays(31);
        fixture.Clock.UtcNow = failCreated;
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_dup_retry_fail_1",
            StripeEventCreatedUtc: failCreated,
            StripeCustomerId: "cus_dup_retry_1",
            StripeSubscriptionId: "sub_dup_retry_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var afterFail = fixture.UserStore.Get(user.UserId)!;
        var graceEnd = Assert.IsType<DateTimeOffset>(afterFail.IndividualGraceEndsAtUtc);
        Assert.Equal("past_due", afterFail.SubscriptionStatus);
        Assert.Equal(AccessMode.Grace, afterFail.EffectiveAccess!.Mode);

        fixture.Clock.UtcNow = failCreated.AddMinutes(1);
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: "evt_dup_retry_fail_1",
            StripeEventCreatedUtc: failCreated,
            StripeCustomerId: "cus_dup_retry_1",
            StripeSubscriptionId: "sub_dup_retry_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        var afterDuplicateFail = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal(graceEnd, afterDuplicateFail.IndividualGraceEndsAtUtc);
        Assert.Equal(AccessMode.Grace, afterDuplicateFail.EffectiveAccess!.Mode);

        fixture.Clock.UtcNow = failCreated.AddHours(2);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: "evt_dup_retry_paid_1",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_dup_retry_1",
            StripeSubscriptionId: "sub_dup_retry_1",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: now.AddDays(60)));

        var recovered = fixture.UserStore.Get(user.UserId)!;
        Assert.Equal("active", recovered.SubscriptionStatus);
        Assert.Null(recovered.IndividualGraceEndsAtUtc);
        Assert.Equal(AccessMode.Full, recovered.EffectiveAccess!.Mode);
        Assert.True(recovered.StripeCurrentPeriodEndUtc > now.AddDays(30));
    }

    [Fact]
    public async Task EndToEnd_CancelScheduledReminderFlow_Should_UseSaveBeforeChurnAndDedupeWindow()
    {
        var now = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(now);

        var periodEnd = now.AddDays(1);
        var user = await SeedActiveIndividualSubscriptionAsync(fixture, "cus_cancel_rem_1", "sub_cancel_rem_1", "driver-cancel-rem@hablotruck.com", "sid_cancel_rem_1");

        fixture.Clock.UtcNow = now.AddMinutes(20);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: "evt_cancel_rem_schedule_1",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: "cus_cancel_rem_1",
            StripeSubscriptionId: "sub_cancel_rem_1",
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: periodEnd,
            CanceledAtUtc: fixture.Clock.UtcNow,
            EndedAtUtc: null));

        await fixture.ReminderService.RunDailyAsync(200);
        await fixture.ReminderService.RunDailyAsync(200);

        var dispatch = Assert.Single(fixture.ManyChat.Reminders);
        Assert.Equal(user.UserId, dispatch.UserId);
        Assert.Equal("save_before_churn_1d", dispatch.ReminderType);
        Assert.Equal("save_before_churn", dispatch.Journey);
        Assert.False(dispatch.UsePositiveContinuityFraming);
        Assert.NotEqual("renewal_reminder_1d", dispatch.ReminderType);
    }

    [Fact]
    public async Task Stress_LongEventStream_Should_EndInDeterministicState()
    {
        var start = Utc(2026, 3, 10, 12);
        var fixture = BuildFixture(start);

        var snapshot = await RunStressEventStreamAsync(fixture, "LONGA", start);

        Assert.Equal("deleted", snapshot.SubscriptionStatus);
        Assert.Equal(AccessMode.Blocked, snapshot.AccessMode);
        Assert.Null(snapshot.GraceEndsAtUtc);
        Assert.False(snapshot.CancelAtPeriodEnd);
        Assert.True(snapshot.LastStripeEventCreatedUtc >= start.AddHours(14));
    }

    [Fact]
    public async Task Stress_LongEventStreamReplay_Should_ConvergeToSameFinalState()
    {
        var start = Utc(2026, 3, 10, 12);

        var baselineFixture = BuildFixture(start);
        var baseline = await RunStressEventStreamAsync(baselineFixture, "BASEL", start);

        var replayFixture = BuildFixture(start);
        var first = await RunStressEventStreamAsync(replayFixture, "REPL", start);
        var second = await RunStressEventStreamAsync(replayFixture, "REPL", start);

        Assert.Equal(baseline, first);
        Assert.Equal(first, second);
    }

    private static async Task<StreamSnapshot> RunStressEventStreamAsync(Fixture fixture, string suffix, DateTimeOffset start)
    {
        fixture.Clock.UtcNow = start;
        var customerId = $"cus_stream_{suffix}";
        var subscriptionId = $"sub_stream_{suffix}";
        var subscriberId = $"sid_stream_{suffix}";

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = $"evt_stream_checkout_{suffix}",
            StripeEventCreatedUtc = start,
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            CustomerEmail = $"driver-stream-{suffix.ToLowerInvariant()}@hablotruck.com",
            PriceId = fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval = "month",
            Metadata = new Dictionary<string, string>
            {
                ["manychatSubscriberId"] = subscriberId,
                ["planType"] = "individual_monthly"
            }
        });

        fixture.Clock.UtcNow = start.AddMinutes(1);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: $"evt_stream_updated_1_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: start.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = start.AddHours(1);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: $"evt_stream_paid_1_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: start.AddDays(60)));

        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: $"evt_stream_paid_1_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: start.AddDays(60)));

        fixture.Clock.UtcNow = start.AddHours(2);
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: $"evt_stream_fail_1_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: $"evt_stream_fail_1_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        fixture.Clock.UtcNow = start.AddHours(3);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: $"evt_stream_paid_2_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: start.AddDays(90)));

        fixture.Clock.UtcNow = start.AddHours(4);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: $"evt_stream_cancel_schedule_1_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: start.AddDays(90),
            CanceledAtUtc: fixture.Clock.UtcNow,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = start.AddHours(5);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: $"evt_stream_old_update_{suffix}",
            StripeEventCreatedUtc: start.AddHours(2),
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            SubscriptionStatus: "past_due",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: start.AddDays(10),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = start.AddHours(6);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: $"evt_stream_paid_3_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: start.AddDays(120)));

        fixture.Clock.UtcNow = start.AddHours(7);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: $"evt_stream_cancel_off_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: start.AddDays(120),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = start.AddHours(8);
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: $"evt_stream_fail_2_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        fixture.Clock.UtcNow = start.AddHours(9);
        await fixture.Handler.HandleInvoicePaidAsync(new StripeInvoicePaid(
            StripeEventId: $"evt_stream_paid_4_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CurrentPeriodEndUtc: start.AddDays(150)));

        fixture.Clock.UtcNow = start.AddHours(10);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: $"evt_stream_cancel_schedule_2_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: true,
            CurrentPeriodEndUtc: start.AddDays(150),
            CanceledAtUtc: fixture.Clock.UtcNow,
            EndedAtUtc: null));

        fixture.Clock.UtcNow = start.AddHours(11);
        await fixture.Handler.HandleInvoicePaymentFailedAsync(new StripeInvoicePaymentFailed(
            StripeEventId: $"evt_stream_fail_old_{suffix}",
            StripeEventCreatedUtc: start.AddHours(8).AddMinutes(-1),
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month"));

        fixture.Clock.UtcNow = start.AddHours(14);
        await fixture.Handler.HandleSubscriptionDeletedAsync(new StripeSubscriptionDeleted(
            StripeEventId: $"evt_stream_deleted_{suffix}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: start.AddHours(13),
            CanceledAtUtc: start.AddHours(13),
            EndedAtUtc: fixture.Clock.UtcNow));

        var user = fixture.UserStore.GetByStripeCustomer(customerId)!;
        return new StreamSnapshot(
            SubscriptionStatus: user.SubscriptionStatus ?? string.Empty,
            CancelAtPeriodEnd: user.StripeCancelAtPeriodEnd,
            CurrentPeriodEndUtc: user.StripeCurrentPeriodEndUtc,
            LastStripeEventCreatedUtc: user.LastStripeEventCreatedUtc ?? DateTimeOffset.MinValue,
            GraceEndsAtUtc: user.IndividualGraceEndsAtUtc,
            AccessMode: user.EffectiveAccess?.Mode ?? AccessMode.Blocked);
    }

    private static async Task<User> SeedActiveIndividualSubscriptionAsync(
        Fixture fixture,
        string customerId,
        string subscriptionId,
        string email,
        string subscriberId)
    {
        var now = fixture.Clock.UtcNow;

        await fixture.Handler.HandleCheckoutCompletedAsync(new StripeEventData
        {
            StripeEventId = $"evt_checkout_{subscriptionId}",
            StripeEventCreatedUtc = now,
            CustomerId = customerId,
            SubscriptionId = subscriptionId,
            CustomerEmail = email,
            PriceId = fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval = "month",
            Metadata = new Dictionary<string, string>
            {
                ["manychatSubscriberId"] = subscriberId,
                ["planType"] = "individual_monthly"
            }
        });

        fixture.Clock.UtcNow = now.AddMinutes(1);
        await fixture.Handler.HandleSubscriptionUpdatedAsync(new StripeSubscriptionUpdate(
            StripeEventId: $"evt_updated_{subscriptionId}",
            StripeEventCreatedUtc: fixture.Clock.UtcNow,
            StripeCustomerId: customerId,
            StripeSubscriptionId: subscriptionId,
            SubscriptionStatus: "active",
            PriceId: fixture.PriceCatalog.IndividualMonthlyPriceId,
            Interval: "month",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: fixture.Clock.UtcNow.AddDays(30),
            CanceledAtUtc: null,
            EndedAtUtc: null));

        return fixture.UserStore.GetByStripeCustomer(customerId)!;
    }

    private static Fixture BuildFixture(DateTimeOffset now)
    {
        var clock = new MutableClock(now);
        var users = new InMemoryUserStore();
        var resolver = new DynamicUserResolver(users);
        var graceIndex = new InMemoryGraceIndexStore();
        var manyChat = new RecordingManyChatSync();
        var failedActions = new NoopFailedActionStore();
        var seats = new NoopSeatAssignmentStore();
        var entitlements = new NoopEntitlementStore();
        var companies = new NoopCompanyStore();
        var expiryIndex = new NoopEntitlementExpiryIndexStore();
        var reminders = new InMemoryReminderStore();

        var orchestrator = new AccessOrchestrator(
            users,
            seats,
            entitlements,
            manyChat,
            failedActions,
            clock,
            new CompanyGracePolicy(7));

        var priceCatalog = new StripeOptions
        {
            WebhookSecret = "whsec_test",
            StripeSecretKey = "sk_test",
            IndividualMonthlyPriceId = "price_ind_monthly",
            IndividualYearlyPriceId = "price_ind_yearly",
            FleetSeatMonthlyPriceId = "price_fleet_monthly"
        };

        var handler = new StripeSubscriptionHandler(
            resolver,
            users,
            graceIndex,
            manyChat,
            orchestrator,
            clock,
            new GracePolicy(72),
            priceCatalog,
            companies,
            entitlements,
            expiryIndex,
            NullLogger<StripeSubscriptionHandler>.Instance);

        var reminderService = new SubscriptionReminderService(
            users,
            companies,
            reminders,
            manyChat,
            clock,
            NullLogger<SubscriptionReminderService>.Instance);

        return new Fixture(clock, handler, reminderService, users, manyChat, priceCatalog);
    }

    private sealed record StreamSnapshot(
        string SubscriptionStatus,
        bool CancelAtPeriodEnd,
        DateTimeOffset? CurrentPeriodEndUtc,
        DateTimeOffset LastStripeEventCreatedUtc,
        DateTimeOffset? GraceEndsAtUtc,
        AccessMode AccessMode);

    private sealed record Fixture(
        MutableClock Clock,
        StripeSubscriptionHandler Handler,
        SubscriptionReminderService ReminderService,
        InMemoryUserStore UserStore,
        RecordingManyChatSync ManyChat,
        StripeOptions PriceCatalog);

    private static DateTimeOffset Utc(int y, int m, int d, int h)
        => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _users = new();
        private int _counter = 0;

        public int Count => _users.Count;

        public void Add(User user)
            => _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public User? Get(string userId)
            => _users.TryGetValue((Buckets.UserBucketPk(userId), userId), out var user) ? user : null;

        public User? GetByStripeCustomer(string stripeCustomerId)
            => _users.Values.FirstOrDefault(x => string.Equals(x.StripeCustomerId, stripeCustomerId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyCollection<User> AllUsers()
            => _users.Values.ToList();

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(_users.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
        {
            var existing = _users.Values.FirstOrDefault(u =>
                (!string.IsNullOrWhiteSpace(emailNormalized) && string.Equals(u.EmailNormalized, emailNormalized, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(manyChatSubscriberId) && string.Equals(u.ManyChatSubscriberId, manyChatSubscriberId, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
                return Task.FromResult(existing);

            _counter++;
            var user = new User
            {
                UserId = $"U_E2E_{_counter:D3}",
                EmailNormalized = string.IsNullOrWhiteSpace(emailNormalized) ? null : emailNormalized.Trim().ToLowerInvariant(),
                ManyChatSubscriberId = string.IsNullOrWhiteSpace(manyChatSubscriberId) ? null : manyChatSubscriberId.Trim(),
                PhoneE164 = phoneE164
            };

            _users[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.FromResult(user);
        }

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
        {
            var rows = _users.Values
                .Where(u => !string.IsNullOrWhiteSpace(u.StripeCustomerId) || !string.IsNullOrWhiteSpace(u.StripeSubscriptionId))
                .Take(take)
                .ToList();

            return Task.FromResult<IReadOnlyList<User>>(rows);
        }
    }

    private sealed class DynamicUserResolver : IUserResolver
    {
        private readonly InMemoryUserStore _users;

        public DynamicUserResolver(InMemoryUserStore users) => _users = users;

        public Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
        {
            var user = _users.GetByStripeCustomer(stripeCustomerId);
            if (user is null) return Task.FromResult<UserRef?>(null);
            return Task.FromResult<UserRef?>(new UserRef(Buckets.UserBucketPk(user.UserId), user.UserId));
        }

        public Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
        {
            var user = _users.AllUsers().FirstOrDefault(x => string.Equals(x.ManyChatSubscriberId, subscriberId, StringComparison.OrdinalIgnoreCase));
            if (user is null) return Task.FromResult<UserRef?>(null);
            return Task.FromResult<UserRef?>(new UserRef(Buckets.UserBucketPk(user.UserId), user.UserId));
        }

        public Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
        {
            var user = _users.AllUsers().FirstOrDefault(x => string.Equals(x.EmailNormalized, emailNormalized, StringComparison.OrdinalIgnoreCase));
            if (user is null) return Task.FromResult<UserRef?>(null);
            return Task.FromResult<UserRef?>(new UserRef(Buckets.UserBucketPk(user.UserId), user.UserId));
        }
    }

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public int SyncCalls { get; private set; }
        public int PaymentFailedFlowCalls { get; private set; }
        public List<SubscriptionReminderDispatch> Reminders { get; } = new();

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
        {
            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
        {
            PaymentFailedFlowCalls++;
            return Task.CompletedTask;
        }

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
        {
            Reminders.Add(dispatch);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryReminderStore : ISubscriptionReminderStore
    {
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> TryMarkSentAsync(string subscriptionId, string reminderType, DateTimeOffset periodEndUtc, DateTimeOffset sentAtUtc, CancellationToken ct = default)
        {
            var key = $"{subscriptionId.Trim()}|{reminderType.Trim().ToLowerInvariant()}|{periodEndUtc.UtcDateTime:yyyyMMdd}";
            return Task.FromResult(_keys.Add(key));
        }
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

    private sealed class NoopEntitlementStore : IEntitlementStore
    {
        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult<Entitlement?>(null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopCompanyStore : ICompanyStore
    {
        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
            => Task.FromResult<Company?>(null);

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult<Company?>(null);

        public Task UpsertAsync(Company company, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopEntitlementExpiryIndexStore : IEntitlementExpiryIndexStore
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
        public Task EnsureTableAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default)
            => Task.CompletedTask;

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
}










