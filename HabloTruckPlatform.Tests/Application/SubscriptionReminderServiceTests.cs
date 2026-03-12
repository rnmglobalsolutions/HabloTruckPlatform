using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class SubscriptionReminderServiceTests
{
    [Fact]
    public async Task RunDailyAsync_Should_SendOnlyDueReminders_AndSkipFutureOrInactive()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_due", "sub_due", "active", "monthly", false, now.AddDays(7), "sid_due"));
        userStore.Users.Add(NewUser("U_future", "sub_future", "active", "monthly", false, now.AddDays(5), "sid_future"));
        userStore.Users.Add(NewUser("U_inactive", "sub_inactive", "past_due", "monthly", false, now.AddDays(7), "sid_inactive"));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("U_due", manyChat.Dispatches[0].UserId);
        Assert.Equal("renewal_reminder_7d", manyChat.Dispatches[0].ReminderType);
    }

    [Fact]
    public async Task RunDailyAsync_Should_SkipAlreadySentReminderWindow()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        var user = NewUser("U_dup", "sub_dup", "active", "monthly", false, now.AddDays(7), "sid_dup");
        userStore.Users.Add(user);

        var reminders = new InMemoryReminderStore();
        reminders.PreMark("sub_dup", "window_7d", now.AddDays(7));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminders, manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Empty(manyChat.Dispatches);
    }

    [Fact]
    public async Task RunDailyAsync_Should_ProcessMixedDueAndAlreadySentWindows_WithoutDuplicating()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_due_new", "sub_due_new", "active", "monthly", false, now.AddDays(7), "sid_due_new"));
        userStore.Users.Add(NewUser("U_due_sent", "sub_due_sent", "active", "monthly", false, now.AddDays(7), "sid_due_sent"));

        var reminders = new InMemoryReminderStore();
        reminders.PreMark("sub_due_sent", "window_7d", now.AddDays(7));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminders, manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("U_due_new", manyChat.Dispatches[0].UserId);
        Assert.Equal("renewal_reminder_7d", manyChat.Dispatches[0].ReminderType);
    }

    [Fact]
    public async Task RunDailyAsync_Should_SendSaveBeforeChurn_WhenCancelAtPeriodEnd()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_cancel", "sub_cancel", "active", "monthly", true, now.AddDays(1), "sid_cancel"));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        var dispatch = manyChat.Dispatches[0];
        Assert.Equal("save_before_churn_1d", dispatch.ReminderType);
        Assert.Equal("save_before_churn", dispatch.Journey);
        Assert.False(dispatch.UsePositiveContinuityFraming);
        Assert.Equal("EndingSoonReactivation", dispatch.ReminderTone);
        Assert.Equal("save_before_churn_ending_soon_1d", dispatch.TemplateKey);
    }

    [Fact]
    public async Task RunDailyAsync_Should_TargetCompanyAdminOnly_ForCompanyPlan()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(new User
        {
            UserId = "U_member",
            EmailNormalized = "member@fleet.com",
            ManyChatSubscriberId = "sid_member",
            StripeCustomerId = "cus_company_1",
            StripeSubscriptionId = "sub_company_1",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            PlanType = "company_seat"
        });

        userStore.Users.Add(new User
        {
            UserId = "U_admin",
            EmailNormalized = "admin@fleet.com",
            ManyChatSubscriberId = "sid_admin",
            StripeCustomerId = "cus_company_1",
            StripeSubscriptionId = "sub_company_1",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            PlanType = "company_seat"
        });

        var companyStore = new InMemoryCompanyStore();
        companyStore.Companies["C_COMPANY_1"] = new Company
        {
            CompanyId = "C_COMPANY_1",
            StripeCustomerId = "cus_company_1",
            AdminEmailNormalized = "admin@fleet.com",
            Status = "active"
        };

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, companyStore, new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("U_admin", manyChat.Dispatches[0].UserId);
        Assert.True(manyChat.Dispatches[0].IsCompanyReminder);
        Assert.Equal("C_COMPANY_1", manyChat.Dispatches[0].CompanyId);
    }

    [Fact]
    public async Task RunDailyAsync_Should_UseSaveBeforeChurnForCompanyCancelScheduled_WithCorrectEndDate()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var endDate = now.AddDays(7);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(new User
        {
            UserId = "U_company_cancel",
            EmailNormalized = "admin@fleet-cancel.com",
            ManyChatSubscriberId = "sid_company_cancel",
            StripeCustomerId = "cus_company_cancel",
            StripeSubscriptionId = "sub_company_cancel",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = endDate,
            PlanType = "company_seat"
        });

        var companyStore = new InMemoryCompanyStore();
        companyStore.Companies["C_COMPANY_CANCEL"] = new Company
        {
            CompanyId = "C_COMPANY_CANCEL",
            StripeCustomerId = "cus_company_cancel",
            AdminEmailNormalized = "admin@fleet-cancel.com",
            Status = "active"
        };

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, companyStore, new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("save_before_churn_7d", manyChat.Dispatches[0].ReminderType);
        Assert.Equal("EndingSoonReactivation", manyChat.Dispatches[0].ReminderTone);
        Assert.Equal("save_before_churn_ending_soon_7d", manyChat.Dispatches[0].TemplateKey);
        Assert.Equal(endDate, manyChat.Dispatches[0].PeriodEndUtc);
        Assert.Equal("C_COMPANY_CANCEL", manyChat.Dispatches[0].CompanyId);
    }

    [Fact]
    public async Task RunDailyAsync_Should_SuppressRenewal_ForPaymentFailedAndDeletedStatuses()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_failed_status", "sub_failed_status", "payment_failed", "monthly", false, now.AddDays(1), "sid_failed_status"));
        userStore.Users.Add(NewUser("U_deleted_status", "sub_deleted_status", "deleted", "monthly", false, now.AddDays(1), "sid_deleted_status"));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Empty(manyChat.Dispatches);
    }

    [Fact]
    public async Task RunDailyAsync_Should_SuppressReminder_WhenPeriodEndIsPast()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_past_due", "sub_past_due", "active", "monthly", false, now.AddDays(-1), "sid_past_due"));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Empty(manyChat.Dispatches);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NotSendDuplicate_WhenRunTwice()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_once", "sub_once", "active", "monthly", false, now.AddDays(7), "sid_once"));

        var reminderStore = new InMemoryReminderStore();
        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminderStore, manyChat, now);

        await sut.RunDailyAsync(take: 100);
        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NotDualSend_WhenJourneyFlipsToCancelScheduled_InSameReminderWindow()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        var user = NewUser("U_flip", "sub_flip", "active", "monthly", false, now.AddDays(1), "sid_flip");
        userStore.Users.Add(user);

        var reminderStore = new InMemoryReminderStore();
        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminderStore, manyChat, now);

        await sut.RunDailyAsync(take: 100);

        user.StripeCancelAtPeriodEnd = true;
        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("renewal_reminder_1d", manyChat.Dispatches[0].ReminderType);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NotDualSend_WhenJourneyFlipsToAutoRenew_InSameReminderWindow()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        var user = NewUser("U_flip_back", "sub_flip_back", "active", "monthly", true, now.AddDays(1), "sid_flip_back");
        userStore.Users.Add(user);

        var reminderStore = new InMemoryReminderStore();
        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminderStore, manyChat, now);

        await sut.RunDailyAsync(take: 100);

        user.StripeCancelAtPeriodEnd = false;
        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("save_before_churn_1d", manyChat.Dispatches[0].ReminderType);
    }

    [Fact]
    public async Task RunDailyAsync_Should_SetPositiveContinuityTone_AndTemplate_ForAutoRenewOneDayReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_pos", "sub_pos", "active", "monthly", false, now.AddDays(1), "sid_pos"));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("renewal_reminder_1d", manyChat.Dispatches[0].ReminderType);
        Assert.True(manyChat.Dispatches[0].UsePositiveContinuityFraming);
        Assert.Equal("PositiveContinuity", manyChat.Dispatches[0].ReminderTone);
        Assert.Equal("renewal_positive_continuity_1d", manyChat.Dispatches[0].TemplateKey);
        Assert.Equal("auto_renew", manyChat.Dispatches[0].Journey);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NeverUsePositiveContinuityTemplate_ForCancelScheduledOneDayReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_cancel_one_day", "sub_cancel_one_day", "active", "monthly", true, now.AddDays(1), "sid_cancel_one_day"));

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        var dispatch = manyChat.Dispatches[0];
        Assert.Equal("save_before_churn_1d", dispatch.ReminderType);
        Assert.False(dispatch.UsePositiveContinuityFraming);
        Assert.Equal("EndingSoonReactivation", dispatch.ReminderTone);
        Assert.NotEqual("renewal_positive_continuity_1d", dispatch.TemplateKey);
    }


    [Fact]
    public async Task RunDailyAsync_Should_SetPositiveContinuityTone_AndTemplate_ForAnnualAutoRenewOneDayReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(new User
        {
            UserId = "U_pos_annual",
            ManyChatSubscriberId = "sid_pos_annual",
            StripeSubscriptionId = "sub_pos_annual",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "annual",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(1),
            PlanType = "individual_yearly"
        });

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        var dispatch = manyChat.Dispatches[0];
        Assert.Equal("annual_renewal_reminder_1d", dispatch.ReminderType);
        Assert.True(dispatch.UsePositiveContinuityFraming);
        Assert.Equal("PositiveContinuity", dispatch.ReminderTone);
        Assert.Equal("annual_renewal_positive_continuity_1d", dispatch.TemplateKey);
        Assert.Equal("auto_renew", dispatch.Journey);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NeverUsePositiveContinuityTemplate_ForAnnualCancelScheduledOneDayReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(new User
        {
            UserId = "U_cancel_annual_one_day",
            ManyChatSubscriberId = "sid_cancel_annual_one_day",
            StripeSubscriptionId = "sub_cancel_annual_one_day",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "annual",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = now.AddDays(1),
            PlanType = "individual_yearly"
        });

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        var dispatch = manyChat.Dispatches[0];
        Assert.Equal("save_before_churn_1d", dispatch.ReminderType);
        Assert.False(dispatch.UsePositiveContinuityFraming);
        Assert.Equal("EndingSoonReactivation", dispatch.ReminderTone);
        Assert.Equal("save_before_churn_ending_soon_1d", dispatch.TemplateKey);
        Assert.NotEqual("annual_renewal_positive_continuity_1d", dispatch.TemplateKey);
        Assert.Equal("save_before_churn", dispatch.Journey);
    }

    [Fact]
    public async Task RunDailyAsync_Should_AllowSameReminderTypeForNewBillingCyclePeriodEnd()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_cycle", "sub_cycle", "active", "monthly", false, now.AddDays(7), "sid_cycle"));

        var reminderStore = new InMemoryReminderStore();
        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminderStore, manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);

        // Simulate next cycle with same reminder type (7d) but different period end date.
        userStore.Users[0].StripeCurrentPeriodEndUtc = now.AddDays(37);

        var sutNextCycle = BuildService(userStore, new InMemoryCompanyStore(), reminderStore, manyChat, now.AddDays(30));
        await sutNextCycle.RunDailyAsync(take: 100);

        Assert.Equal(2, manyChat.Dispatches.Count);
        Assert.All(manyChat.Dispatches, d => Assert.Equal("renewal_reminder_7d", d.ReminderType));
        Assert.NotEqual(manyChat.Dispatches[0].PeriodEndUtc, manyChat.Dispatches[1].PeriodEndUtc);
    }

    [Fact]
    public async Task RunDailyAsync_Should_Skip_WhenSubscriberOrSubscriptionIdMissing()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_missing_sid", "sub_missing_sid", "active", "monthly", false, now.AddDays(7), subscriberId: ""));
        userStore.Users.Add(new User
        {
            UserId = "U_missing_sub",
            ManyChatSubscriberId = "sid_missing_sub",
            StripeSubscriptionId = "",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            PlanType = "individual_monthly"
        });

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Empty(manyChat.Dispatches);
    }
    [Fact]
    public async Task RunDailyAsync_Should_RemainIdempotent_AfterPartialFailureAndRerun()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_fail_once", "sub_fail_once", "active", "monthly", false, now.AddDays(7), "sid_fail_once"));
        userStore.Users.Add(NewUser("U_success_once", "sub_success_once", "active", "monthly", false, now.AddDays(7), "sid_success_once"));

        var reminderStore = new InMemoryReminderStore();
        var manyChat = new RecordingManyChatSync();
        manyChat.FailSubscriberIds.Add("sid_fail_once");

        var sut = BuildService(userStore, new InMemoryCompanyStore(), reminderStore, manyChat, now);

        await sut.RunDailyAsync(take: 100);
        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("U_success_once", manyChat.Dispatches[0].UserId);
        Assert.Equal("renewal_reminder_7d", manyChat.Dispatches[0].ReminderType);
    }

    [Fact]
    public async Task RunDailyAsync_Should_ContinueProcessing_WhenOneReminderDispatchThrows()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_throw", "sub_throw", "active", "monthly", false, now.AddDays(7), "sid_throw"));
        userStore.Users.Add(NewUser("U_ok", "sub_ok", "active", "monthly", false, now.AddDays(7), "sid_ok"));

        var manyChat = new RecordingManyChatSync();
        manyChat.FailSubscriberIds.Add("sid_throw");

        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Equal("U_ok", manyChat.Dispatches[0].UserId);
        Assert.Equal("renewal_reminder_7d", manyChat.Dispatches[0].ReminderType);
    }

    [Fact]
    public async Task RunDailyAsync_Should_EnqueueFailedAction_WhenManyChatFailureIsRetryable()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_retryable", "sub_retryable", "active", "monthly", false, now.AddDays(7), "sid_retryable"));

        var manyChat = new RecordingManyChatSync();
        manyChat.ExceptionsBySubscriberId["sid_retryable"] = new ManyChatRequestException(
            path: "fb/sending/sendFlow",
            statusCode: HttpStatusCode.ServiceUnavailable,
            isRetryable: true,
            failureCategory: ManyChatFailureCategory.TransientHttp,
            message: "retryable");

        var failedActions = new InMemoryFailedActionStore();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now, failedActions);

        await sut.RunDailyAsync(take: 100);

        var queued = Assert.Single(failedActions.Enqueued);
        Assert.Equal(FailedActionRetryService.ActionManyChatSubscriptionReminder, queued.ActionType);
        Assert.Empty(manyChat.Dispatches);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NotEnqueueFailedAction_WhenManyChatFailureIsNonRetryable()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_non_retryable", "sub_non_retryable", "active", "monthly", false, now.AddDays(7), "sid_non_retryable"));

        var manyChat = new RecordingManyChatSync();
        manyChat.ExceptionsBySubscriberId["sid_non_retryable"] = new ManyChatRequestException(
            path: "fb/sending/sendFlow",
            statusCode: HttpStatusCode.BadRequest,
            isRetryable: false,
            failureCategory: ManyChatFailureCategory.PermanentHttp,
            message: "non_retryable");

        var failedActions = new InMemoryFailedActionStore();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now, failedActions);

        await sut.RunDailyAsync(take: 100);

        Assert.Empty(failedActions.Enqueued);
        Assert.Empty(manyChat.Dispatches);
    }

    [Fact]
    public async Task RunDailyAsync_Should_NotEnqueueFailedAction_WhenManyChatSendSucceeds()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(NewUser("U_ok_no_queue", "sub_ok_no_queue", "active", "monthly", false, now.AddDays(7), "sid_ok_no_queue"));

        var manyChat = new RecordingManyChatSync();
        var failedActions = new InMemoryFailedActionStore();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now, failedActions);

        await sut.RunDailyAsync(take: 100);

        Assert.Single(manyChat.Dispatches);
        Assert.Empty(failedActions.Enqueued);
    }

    [Fact]
    public async Task RunDailyAsync_Should_DeriveAudienceSegment_FromRecentVsStaleActivityMarkers()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var userStore = new InMemoryUserStore();
        userStore.Users.Add(new User
        {
            UserId = "U_segment_active",
            ManyChatSubscriberId = "sid_segment_active",
            StripeSubscriptionId = "sub_segment_active",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            PlanType = "individual_monthly",
            LastManyChatSyncAtUtc = now.AddDays(-5)
        });

        userStore.Users.Add(new User
        {
            UserId = "U_segment_risk",
            ManyChatSubscriberId = "sid_segment_risk",
            StripeSubscriptionId = "sub_segment_risk",
            SubscriptionStatus = "active",
            IndividualPlanTerm = "monthly",
            StripeCancelAtPeriodEnd = false,
            StripeCurrentPeriodEndUtc = now.AddDays(7),
            PlanType = "individual_monthly",
            LastManyChatSyncAtUtc = now.AddDays(-45)
        });

        var manyChat = new RecordingManyChatSync();
        var sut = BuildService(userStore, new InMemoryCompanyStore(), new InMemoryReminderStore(), manyChat, now);

        await sut.RunDailyAsync(take: 100);

        Assert.Equal(2, manyChat.Dispatches.Count);

        var byUser = manyChat.Dispatches.ToDictionary(d => d.UserId, d => d.AudienceSegment, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("active", byUser["U_segment_active"]);
        Assert.Equal("at_risk", byUser["U_segment_risk"]);
    }

    [Fact]
    public async Task RunDailyAsync_Should_Throw_WhenTakeIsNotPositive()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var sut = BuildService(new InMemoryUserStore(), new InMemoryCompanyStore(), new InMemoryReminderStore(), new RecordingManyChatSync(), now);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.RunDailyAsync(take: 0));
    }

    private static SubscriptionReminderService BuildService(
        InMemoryUserStore users,
        InMemoryCompanyStore companies,
        InMemoryReminderStore reminders,
        RecordingManyChatSync manyChat,
        DateTimeOffset now,
        InMemoryFailedActionStore? failedActionStore = null)
    {
        return new SubscriptionReminderService(
            users,
            companies,
            reminders,
            manyChat,
            failedActionStore ?? new InMemoryFailedActionStore(),
            new FixedClock(now),
            NullLogger<SubscriptionReminderService>.Instance);
    }

    private static User NewUser(
        string userId,
        string subscriptionId,
        string status,
        string term,
        bool cancelAtPeriodEnd,
        DateTimeOffset periodEnd,
        string subscriberId)
    {
        return new User
        {
            UserId = userId,
            ManyChatSubscriberId = subscriberId,
            StripeSubscriptionId = subscriptionId,
            SubscriptionStatus = status,
            IndividualPlanTerm = term,
            StripeCancelAtPeriodEnd = cancelAtPeriodEnd,
            StripeCurrentPeriodEndUtc = periodEnd,
            PlanType = "individual_monthly"
        };
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        public List<User> Users { get; } = new();

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.UserId == userId));

        public Task UpsertAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Users.Take(take).ToList());
    }

    private sealed class InMemoryCompanyStore : ICompanyStore
    {
        public Dictionary<string, Company> Companies { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
        {
            Companies.TryGetValue(companyId, out var company);
            return Task.FromResult(company);
        }

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
        {
            var company = Companies.Values.FirstOrDefault(c => string.Equals(c.StripeCustomerId, stripeCustomerId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(company);
        }

        public Task UpsertAsync(Company company, CancellationToken ct = default)
        {
            Companies[company.CompanyId] = company;
            return Task.CompletedTask;
        }

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryReminderStore : ISubscriptionReminderStore
    {
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

        public void PreMark(string subscriptionId, string reminderType, DateTimeOffset periodEndUtc)
            => _keys.Add(Key(subscriptionId, reminderType, periodEndUtc));

        public Task<bool> TryMarkSentAsync(string subscriptionId, string reminderType, DateTimeOffset periodEndUtc, DateTimeOffset sentAtUtc, CancellationToken ct = default)
        {
            var key = Key(subscriptionId, reminderType, periodEndUtc);
            var added = _keys.Add(key);
            return Task.FromResult(added);
        }

        private static string Key(string subscriptionId, string reminderType, DateTimeOffset periodEndUtc)
            => $"{subscriptionId.Trim()}|{reminderType.Trim().ToLowerInvariant()}|{periodEndUtc.UtcDateTime:yyyyMMdd}";
    }

    private sealed class InMemoryFailedActionStore : IFailedActionStore
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

    private sealed class RecordingManyChatSync : IManyChatSync
    {
        public List<SubscriptionReminderDispatch> Dispatches { get; } = new();
        public HashSet<string> FailSubscriberIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Exception> ExceptionsBySubscriberId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default)
        {
            if (ExceptionsBySubscriberId.TryGetValue(dispatch.SubscriberId, out var ex))
                throw ex;

            if (FailSubscriberIds.Contains(dispatch.SubscriberId))
                throw new InvalidOperationException("simulated_send_failure");

            Dispatches.Add(dispatch);
            return Task.CompletedTask;
        }
    }
}






