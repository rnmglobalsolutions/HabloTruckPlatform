using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Billing;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class SubscriptionReminderService
{
    private readonly IUserStore _users;
    private readonly ICompanyStore _companies;
    private readonly ISubscriptionReminderStore _reminders;
    private readonly IManyChatSync _manyChat;
    private readonly IClock _clock;
    private readonly ILogger<SubscriptionReminderService> _logger;

    public SubscriptionReminderService(
        IUserStore users,
        ICompanyStore companies,
        ISubscriptionReminderStore reminders,
        IManyChatSync manyChat,
        IClock clock,
        ILogger<SubscriptionReminderService> logger)
    {
        _users = users;
        _companies = companies;
        _reminders = reminders;
        _manyChat = manyChat;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunDailyAsync(int take = 2000, CancellationToken ct = default)
    {
        if (take <= 0)
            throw new ArgumentOutOfRangeException(nameof(take));

        var nowUtc = _clock.UtcNow;
        var users = await _users.QueryUsersWithStripeAsync(take, ct);

        var scanned = 0;
        var due = 0;
        var sent = 0;

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;

            var dispatch = await BuildDispatchAsync(user, nowUtc, ct);
            if (dispatch is null)
                continue;

            due++;

            // Idempotency is enforced at the reminder-window level (for example 7d/3d/1d),
            // so journey flips (auto-renew <-> cancel-scheduled) cannot double-send
            // for the same subscription and billing period.
            var windowKey = BuildReminderWindowKey(dispatch);
            var firstTime = await _reminders.TryMarkSentAsync(
                dispatch.SubscriptionId,
                windowKey,
                dispatch.PeriodEndUtc,
                nowUtc,
                ct);

            if (!firstTime)
                continue;

            try
            {
                await _manyChat.SendSubscriptionReminderAsync(dispatch, ct);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Subscription reminder dispatch failed. userId={UserId} subscriptionId={SubscriptionId} reminderType={ReminderType}",
                    user.UserId,
                    dispatch.SubscriptionId,
                    dispatch.ReminderType);
            }
        }

        _logger.LogInformation(
            "SubscriptionReminderService run completed. scanned={Scanned} due={Due} sent={Sent}",
            scanned,
            due,
            sent);
    }

    private static string BuildReminderWindowKey(SubscriptionReminderDispatch dispatch)
    {
        var days = Math.Max(0, dispatch.DaysUntilPeriodEnd);
        return $"window_{days}d";
    }

    private async Task<SubscriptionReminderDispatch?> BuildDispatchAsync(
        User user,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            return null;

        if (string.IsNullOrWhiteSpace(user.StripeSubscriptionId))
            return null;

        var facts = new SubscriptionReminderFacts(
            SubscriptionId: user.StripeSubscriptionId,
            Status: user.SubscriptionStatus,
            PlanTerm: user.IndividualPlanTerm,
            CancelAtPeriodEnd: user.StripeCancelAtPeriodEnd,
            CurrentPeriodEndUtc: user.StripeCurrentPeriodEndUtc,
            PlanType: user.PlanType);

        var decision = SubscriptionReminderEvaluator.Evaluate(facts, nowUtc);
        if (decision is null)
            return null;

        var isCompanyReminder = IsCompanyPlan(user.PlanType);
        string? companyId = null;

        if (isCompanyReminder)
        {
            var company = await ResolveCompanyAsync(user, ct);
            if (company is null)
                return null;

            if (!IsAdminTarget(user, company))
                return null;

            companyId = company.CompanyId;
        }

        var segment = DeriveAudienceSegment(user, nowUtc);

        return new SubscriptionReminderDispatch(
            SubscriberId: user.ManyChatSubscriberId!.Trim(),
            UserId: user.UserId,
            SubscriptionId: user.StripeSubscriptionId!.Trim(),
            ReminderType: decision.Kind.ToEventName(),
            Journey: decision.Journey == SubscriptionReminderJourney.SaveBeforeChurn ? "save_before_churn" : "auto_renew",
            DaysUntilPeriodEnd: decision.DaysUntilPeriodEnd,
            PeriodEndUtc: decision.PeriodEndUtc,
            UsePositiveContinuityFraming: decision.UsePositiveContinuityFraming,
            ReminderTone: decision.Tone.ToString(),
            TemplateKey: decision.TemplateKey,
            AudienceSegment: segment,
            IsCompanyReminder: isCompanyReminder,
            CompanyId: companyId,
            PlanTerm: user.IndividualPlanTerm
        );
    }

    private async Task<Company?> ResolveCompanyAsync(User user, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(user.CompanyId))
        {
            var company = await _companies.GetAsync(user.CompanyId!, ct);
            if (company is not null)
                return company;
        }

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
            return await _companies.GetByStripeCustomerIdAsync(user.StripeCustomerId!, ct);

        return null;
    }

    private static bool IsAdminTarget(User user, Company company)
    {
        if (string.IsNullOrWhiteSpace(company.AdminEmailNormalized))
            return true; // fallback to the current subscription owner user

        if (string.IsNullOrWhiteSpace(user.EmailNormalized))
            return false;

        return string.Equals(
            user.EmailNormalized,
            company.AdminEmailNormalized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanyPlan(string? planType)
    {
        var plan = string.IsNullOrWhiteSpace(planType) ? null : planType.Trim().ToLowerInvariant();
        return plan is "company_seat" or "fleet" or "fleet_seat";
    }

    private static string DeriveAudienceSegment(User user, DateTimeOffset nowUtc)
    {
        // Usage telemetry is not currently persisted in user projections.
        // We use the latest known engagement proxy as a lightweight segment hook.
        var markerUtc = user.LastManyChatSyncAtUtc ?? user.UpdatedAtUtc;
        if (markerUtc is null)
            return "at_risk";

        var activeWindow = nowUtc.AddDays(-30);
        return markerUtc.Value >= activeWindow ? "active" : "at_risk";
    }
}




