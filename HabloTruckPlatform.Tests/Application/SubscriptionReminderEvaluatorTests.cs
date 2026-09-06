using HabloTruckPlatform.Application.Billing;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class SubscriptionReminderEvaluatorTests
{
    [Theory]
    [InlineData(7, SubscriptionReminderKind.RenewalReminder7d)]
    [InlineData(3, SubscriptionReminderKind.RenewalReminder3d)]
    [InlineData(1, SubscriptionReminderKind.RenewalReminder1d)]
    public void Evaluate_Should_ReturnMonthlyAutoRenewReminder_ForDueWindows(int daysUntil, SubscriptionReminderKind expected)
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "monthly",
            cancelAtPeriodEnd: false,
            periodEnd: now.AddDays(daysUntil));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Kind);
        Assert.Equal(SubscriptionReminderJourney.AutoRenewContinuity, result.Journey);
    }

    [Theory]
    [InlineData(30, SubscriptionReminderKind.AnnualRenewalReminder30d)]
    [InlineData(14, SubscriptionReminderKind.AnnualRenewalReminder14d)]
    [InlineData(7, SubscriptionReminderKind.AnnualRenewalReminder7d)]
    [InlineData(1, SubscriptionReminderKind.AnnualRenewalReminder1d)]
    public void Evaluate_Should_ReturnAnnualAutoRenewReminder_ForDueWindows(int daysUntil, SubscriptionReminderKind expected)
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "annual",
            cancelAtPeriodEnd: false,
            periodEnd: now.AddDays(daysUntil),
            planType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Kind);
        Assert.Equal(SubscriptionReminderJourney.AutoRenewContinuity, result.Journey);
    }

    [Theory]
    [InlineData(7, SubscriptionReminderKind.SaveBeforeChurn7d)]
    [InlineData(3, SubscriptionReminderKind.SaveBeforeChurn3d)]
    [InlineData(1, SubscriptionReminderKind.SaveBeforeChurn1d)]
    public void Evaluate_Should_ReturnMonthlySaveBeforeChurnReminder_WhenCancelAtPeriodEnd(int daysUntil, SubscriptionReminderKind expected)
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "monthly",
            cancelAtPeriodEnd: true,
            periodEnd: now.AddDays(daysUntil));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Kind);
        Assert.Equal(SubscriptionReminderJourney.SaveBeforeChurn, result.Journey);
        Assert.False(result.UsePositiveContinuityFraming);
        Assert.Equal(SubscriptionReminderTone.EndingSoonReactivation, result.Tone);
    }

    [Theory]
    [InlineData(30, SubscriptionReminderKind.SaveBeforeChurn30d)]
    [InlineData(14, SubscriptionReminderKind.SaveBeforeChurn14d)]
    [InlineData(7, SubscriptionReminderKind.SaveBeforeChurn7d)]
    [InlineData(1, SubscriptionReminderKind.SaveBeforeChurn1d)]
    public void Evaluate_Should_ReturnAnnualSaveBeforeChurnReminder_WhenCancelAtPeriodEnd(int daysUntil, SubscriptionReminderKind expected)
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "annual",
            cancelAtPeriodEnd: true,
            periodEnd: now.AddDays(daysUntil),
            planType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(expected, result!.Kind);
        Assert.Equal(SubscriptionReminderJourney.SaveBeforeChurn, result.Journey);
        Assert.Equal(SubscriptionReminderTone.EndingSoonReactivation, result.Tone);
    }

    [Fact]
    public void Evaluate_Should_UsePositiveContinuityFraming_ForMonthlyAutoRenewOneDayReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: false, periodEnd: now.AddDays(1));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderKind.RenewalReminder1d, result!.Kind);
        Assert.True(result.UsePositiveContinuityFraming);
        Assert.Equal(SubscriptionReminderTone.PositiveContinuity, result.Tone);
        Assert.Equal("renewal_reminder_1d", result.Kind.ToEventName());
        Assert.Equal("renewal_positive_continuity_1d", result.TemplateKey);
    }

    [Fact]
    public void Evaluate_Should_UsePositiveContinuityFraming_ForAnnualAutoRenewOneDayReminder()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(status: "active", term: "annual", cancelAtPeriodEnd: false, periodEnd: now.AddDays(1), planType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderKind.AnnualRenewalReminder1d, result!.Kind);
        Assert.True(result.UsePositiveContinuityFraming);
        Assert.Equal(SubscriptionReminderTone.PositiveContinuity, result.Tone);
        Assert.Equal("annual_renewal_reminder_1d", result.Kind.ToEventName());
        Assert.Equal("annual_renewal_positive_continuity_1d", result.TemplateKey);
    }

    [Fact]
    public void Evaluate_Should_NotUsePositiveRenewalReminder_WhenCancelScheduledAtOneDay()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: true, periodEnd: now.AddDays(1));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderKind.SaveBeforeChurn1d, result!.Kind);
        Assert.False(result.UsePositiveContinuityFraming);
        Assert.Equal(SubscriptionReminderTone.EndingSoonReactivation, result.Tone);
        Assert.Equal("save_before_churn_1d", result.Kind.ToEventName());
        Assert.Equal("save_before_churn_ending_soon_1d", result.TemplateKey);
    }

    [Fact]
    public void Evaluate_Should_UseRenewalTransparencyTone_ForNonOneDayAutoRenew()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: false, periodEnd: now.AddDays(7));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderKind.RenewalReminder7d, result!.Kind);
        Assert.False(result.UsePositiveContinuityFraming);
        Assert.Equal(SubscriptionReminderTone.RenewalTransparencyValue, result.Tone);
        Assert.Equal("renewal_transparency_value_7d", result.TemplateKey);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("past_due")]
    [InlineData("unpaid")]
    [InlineData("incomplete")]
    public void Evaluate_Should_ReturnNull_ForInactiveOrDelinquentStatuses(string status)
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(status: status, term: "monthly", cancelAtPeriodEnd: false, periodEnd: now.AddDays(7));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_Should_ReturnNull_WhenWindowNotDueOrPeriodAlreadyEnded()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        var notDue = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: false, periodEnd: now.AddDays(5));
        var ended = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: false, periodEnd: now.AddHours(-1));

        Assert.Null(SubscriptionReminderEvaluator.Evaluate(notDue, now));
        Assert.Null(SubscriptionReminderEvaluator.Evaluate(ended, now));
    }

    [Fact]
    public void Evaluate_Should_NotDualSendRenewalAndSaveBeforeChurn_ForCancelScheduledSubscription()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: true, periodEnd: now.AddDays(7));

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderJourney.SaveBeforeChurn, result!.Journey);
        Assert.Equal(SubscriptionReminderKind.SaveBeforeChurn7d, result.Kind);
        Assert.NotEqual(SubscriptionReminderTone.PositiveContinuity, result.Tone);
    }


    [Fact]
    public void Evaluate_Should_DetectAnnualFromPlanType_WhenPlanTermMissing()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = new SubscriptionReminderFacts(
            SubscriptionId: "sub_annual_from_plan",
            Status: "active",
            PlanTerm: null,
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(14),
            PlanType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderKind.AnnualRenewalReminder14d, result!.Kind);
    }

    [Fact]
    public void Evaluate_Should_ReturnNull_ForAnnualWhenMonthlyOnlyWindow()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "annual",
            cancelAtPeriodEnd: false,
            periodEnd: now.AddDays(3),
            planType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_Should_ReturnNull_WhenSubscriptionIdMissing()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = new SubscriptionReminderFacts(
            SubscriptionId: " ",
            Status: "active",
            PlanTerm: "monthly",
            CancelAtPeriodEnd: false,
            CurrentPeriodEndUtc: now.AddDays(7),
            PlanType: "individual_monthly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.Null(result);
    }
    [Fact]
    public void Evaluate_Should_TreatYearlyPlanTermAsAnnual_ForWindowMatching()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "yearly",
            cancelAtPeriodEnd: false,
            periodEnd: now.AddDays(30),
            planType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionReminderKind.AnnualRenewalReminder30d, result!.Kind);
        Assert.Equal(SubscriptionReminderJourney.AutoRenewContinuity, result.Journey);
    }

    [Fact]
    public void Evaluate_Should_ReturnNull_ForAnnualSaveBeforeChurn_WhenThreeDayWindowUsed()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var facts = NewFacts(
            status: "active",
            term: "annual",
            cancelAtPeriodEnd: true,
            periodEnd: now.AddDays(3),
            planType: "individual_yearly");

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_Should_UseDateBoundaries_ForOneDayWindowDeterministically()
    {
        var now = new DateTimeOffset(2026, 3, 10, 23, 50, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 3, 11, 0, 5, 0, TimeSpan.Zero);
        var facts = NewFacts(status: "active", term: "monthly", cancelAtPeriodEnd: false, periodEnd: periodEnd);

        var result = SubscriptionReminderEvaluator.Evaluate(facts, now);

        Assert.NotNull(result);
        Assert.Equal(1, result!.DaysUntilPeriodEnd);
        Assert.Equal(SubscriptionReminderKind.RenewalReminder1d, result.Kind);
    }

    [Fact]
    public void Evaluate_Should_Throw_ForNullFacts()
    {
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentNullException>(() => SubscriptionReminderEvaluator.Evaluate(null!, now));
    }

    private static SubscriptionReminderFacts NewFacts(
        string status,
        string term,
        bool cancelAtPeriodEnd,
        DateTimeOffset periodEnd,
        string? planType = null)
    {
        return new SubscriptionReminderFacts(
            SubscriptionId: "sub_test_1",
            Status: status,
            PlanTerm: term,
            CancelAtPeriodEnd: cancelAtPeriodEnd,
            CurrentPeriodEndUtc: periodEnd,
            PlanType: planType);
    }
}


