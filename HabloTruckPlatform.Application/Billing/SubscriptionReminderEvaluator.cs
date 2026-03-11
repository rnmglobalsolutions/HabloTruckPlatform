namespace HabloTruckPlatform.Application.Billing;

public static class SubscriptionReminderEvaluator
{
    public static SubscriptionReminderDecision? Evaluate(
        SubscriptionReminderFacts facts,
        DateTimeOffset nowUtc)
    {
        if (facts is null)
            throw new ArgumentNullException(nameof(facts));

        if (string.IsNullOrWhiteSpace(facts.SubscriptionId))
            return null;

        if (facts.CurrentPeriodEndUtc is null)
            return null;

        var periodEndUtc = facts.CurrentPeriodEndUtc.Value.ToUniversalTime();
        if (periodEndUtc <= nowUtc)
            return null;

        var status = Normalize(facts.Status);
        var activeLike = status is "active" or "trialing";
        if (!activeLike)
            return null;

        var daysUntil = (periodEndUtc.UtcDateTime.Date - nowUtc.UtcDateTime.Date).Days;
        if (daysUntil <= 0)
            return null;

        var isAnnual = IsAnnual(facts.PlanTerm, facts.PlanType);

        if (facts.CancelAtPeriodEnd)
        {
            var saveKind = isAnnual
                ? daysUntil switch
                {
                    30 => SubscriptionReminderKind.SaveBeforeChurn30d,
                    14 => SubscriptionReminderKind.SaveBeforeChurn14d,
                    7 => SubscriptionReminderKind.SaveBeforeChurn7d,
                    1 => SubscriptionReminderKind.SaveBeforeChurn1d,
                    _ => (SubscriptionReminderKind?)null
                }
                : daysUntil switch
                {
                    7 => SubscriptionReminderKind.SaveBeforeChurn7d,
                    3 => SubscriptionReminderKind.SaveBeforeChurn3d,
                    1 => SubscriptionReminderKind.SaveBeforeChurn1d,
                    _ => (SubscriptionReminderKind?)null
                };

            if (saveKind is null)
                return null;

            return new SubscriptionReminderDecision(
                Kind: saveKind.Value,
                Journey: SubscriptionReminderJourney.SaveBeforeChurn,
                DaysUntilPeriodEnd: daysUntil,
                PeriodEndUtc: periodEndUtc,
                UsePositiveContinuityFraming: false,
                Tone: SubscriptionReminderTone.EndingSoonReactivation,
                TemplateKey: saveKind.Value.ToTemplateKey());
        }

        var renewalKind = isAnnual
            ? daysUntil switch
            {
                30 => SubscriptionReminderKind.AnnualRenewalReminder30d,
                14 => SubscriptionReminderKind.AnnualRenewalReminder14d,
                7 => SubscriptionReminderKind.AnnualRenewalReminder7d,
                1 => SubscriptionReminderKind.AnnualRenewalReminder1d,
                _ => (SubscriptionReminderKind?)null
            }
            : daysUntil switch
            {
                7 => SubscriptionReminderKind.RenewalReminder7d,
                3 => SubscriptionReminderKind.RenewalReminder3d,
                1 => SubscriptionReminderKind.RenewalReminder1d,
                _ => (SubscriptionReminderKind?)null
            };

        if (renewalKind is null)
            return null;

        var positiveContinuity = renewalKind is SubscriptionReminderKind.RenewalReminder1d
            or SubscriptionReminderKind.AnnualRenewalReminder1d;

        var tone = positiveContinuity
            ? SubscriptionReminderTone.PositiveContinuity
            : SubscriptionReminderTone.RenewalTransparencyValue;

        return new SubscriptionReminderDecision(
            Kind: renewalKind.Value,
            Journey: SubscriptionReminderJourney.AutoRenewContinuity,
            DaysUntilPeriodEnd: daysUntil,
            PeriodEndUtc: periodEndUtc,
            UsePositiveContinuityFraming: positiveContinuity,
            Tone: tone,
            TemplateKey: renewalKind.Value.ToTemplateKey());
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static bool IsAnnual(string? term, string? planType)
    {
        var t = Normalize(term);
        if (t is "annual" or "yearly" or "year")
            return true;

        var p = Normalize(planType);
        if (p is null)
            return false;

        return p.Contains("annual", StringComparison.OrdinalIgnoreCase)
               || p.Contains("yearly", StringComparison.OrdinalIgnoreCase)
               || p.EndsWith("_year", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SubscriptionReminderFacts(
    string? SubscriptionId,
    string? Status,
    string? PlanTerm,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEndUtc,
    string? PlanType
);

public sealed record SubscriptionReminderDecision(
    SubscriptionReminderKind Kind,
    SubscriptionReminderJourney Journey,
    int DaysUntilPeriodEnd,
    DateTimeOffset PeriodEndUtc,
    bool UsePositiveContinuityFraming,
    SubscriptionReminderTone Tone,
    string TemplateKey);

public enum SubscriptionReminderJourney
{
    AutoRenewContinuity = 1,
    SaveBeforeChurn = 2
}

public enum SubscriptionReminderTone
{
    RenewalTransparencyValue = 1,
    PositiveContinuity = 2,
    EndingSoonReactivation = 3
}

public enum SubscriptionReminderKind
{
    RenewalReminder7d = 1,
    RenewalReminder3d = 2,
    RenewalReminder1d = 3,

    AnnualRenewalReminder30d = 4,
    AnnualRenewalReminder14d = 5,
    AnnualRenewalReminder7d = 6,
    AnnualRenewalReminder1d = 7,

    SaveBeforeChurn30d = 8,
    SaveBeforeChurn14d = 9,
    SaveBeforeChurn7d = 10,
    SaveBeforeChurn3d = 11,
    SaveBeforeChurn1d = 12,
}

public static class SubscriptionReminderKindNames
{
    public static string ToEventName(this SubscriptionReminderKind kind)
    {
        return kind switch
        {
            SubscriptionReminderKind.RenewalReminder7d => "renewal_reminder_7d",
            SubscriptionReminderKind.RenewalReminder3d => "renewal_reminder_3d",
            SubscriptionReminderKind.RenewalReminder1d => "renewal_reminder_1d",

            SubscriptionReminderKind.AnnualRenewalReminder30d => "annual_renewal_reminder_30d",
            SubscriptionReminderKind.AnnualRenewalReminder14d => "annual_renewal_reminder_14d",
            SubscriptionReminderKind.AnnualRenewalReminder7d => "annual_renewal_reminder_7d",
            SubscriptionReminderKind.AnnualRenewalReminder1d => "annual_renewal_reminder_1d",

            SubscriptionReminderKind.SaveBeforeChurn30d => "save_before_churn_30d",
            SubscriptionReminderKind.SaveBeforeChurn14d => "save_before_churn_14d",
            SubscriptionReminderKind.SaveBeforeChurn7d => "save_before_churn_7d",
            SubscriptionReminderKind.SaveBeforeChurn3d => "save_before_churn_3d",
            SubscriptionReminderKind.SaveBeforeChurn1d => "save_before_churn_1d",
            _ => "renewal_reminder_unknown"
        };
    }

    public static string ToTemplateKey(this SubscriptionReminderKind kind)
    {
        return kind switch
        {
            SubscriptionReminderKind.RenewalReminder7d => "renewal_transparency_value_7d",
            SubscriptionReminderKind.RenewalReminder3d => "renewal_transparency_value_3d",
            SubscriptionReminderKind.RenewalReminder1d => "renewal_positive_continuity_1d",

            SubscriptionReminderKind.AnnualRenewalReminder30d => "annual_renewal_transparency_value_30d",
            SubscriptionReminderKind.AnnualRenewalReminder14d => "annual_renewal_transparency_value_14d",
            SubscriptionReminderKind.AnnualRenewalReminder7d => "annual_renewal_transparency_value_7d",
            SubscriptionReminderKind.AnnualRenewalReminder1d => "annual_renewal_positive_continuity_1d",

            SubscriptionReminderKind.SaveBeforeChurn30d => "save_before_churn_ending_soon_30d",
            SubscriptionReminderKind.SaveBeforeChurn14d => "save_before_churn_ending_soon_14d",
            SubscriptionReminderKind.SaveBeforeChurn7d => "save_before_churn_ending_soon_7d",
            SubscriptionReminderKind.SaveBeforeChurn3d => "save_before_churn_ending_soon_3d",
            SubscriptionReminderKind.SaveBeforeChurn1d => "save_before_churn_ending_soon_1d",
            _ => "renewal_template_unknown"
        };
    }
}
