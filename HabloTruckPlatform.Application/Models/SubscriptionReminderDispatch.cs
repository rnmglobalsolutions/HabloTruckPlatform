namespace HabloTruckPlatform.Application.Models;

public sealed record SubscriptionReminderDispatch(
    string SubscriberId,
    string UserId,
    string SubscriptionId,
    string ReminderType,
    string Journey,
    int DaysUntilPeriodEnd,
    DateTimeOffset PeriodEndUtc,
    bool UsePositiveContinuityFraming,
    string ReminderTone,
    string TemplateKey,
    string AudienceSegment,
    bool IsCompanyReminder,
    string? CompanyId,
    string? PlanTerm
);
