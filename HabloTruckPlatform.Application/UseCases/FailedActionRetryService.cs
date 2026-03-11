using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Access;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class FailedActionRetryService
{
    public const string ActionManyChatSync = "manychat.sync";

    private readonly IFailedActionStore _store;
    private readonly IUserStore _users;
    private readonly IManyChatSync _manyChat;
    private readonly ILogger<FailedActionRetryService> _logger;

    public FailedActionRetryService(
        IFailedActionStore store,
        IUserStore users,
        IManyChatSync manyChat,
        ILogger<FailedActionRetryService>? logger = null)
    {
        _store = store;
        _users = users;
        _manyChat = manyChat;
        _logger = logger ?? NullLogger<FailedActionRetryService>.Instance;
    }

    public async Task RetryDueAsync(int lookbackHours, int take, CancellationToken ct = default)
    {
        var opWatch = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Operation started. LogCategory={LogCategory} OperationName={OperationName} LookbackHours={LookbackHours} Take={Take}",
            "entry",
            "failed_action_retry",
            lookbackHours,
            take);

        var queryWatch = Stopwatch.StartNew();
        var due = await _store.GetDueAsync(now, lookbackHours: lookbackHours, take: take, ct);

        _logger.LogDebug(
            "Persistence read completed. LogCategory={LogCategory} PersistenceOperation={PersistenceOperation} Target={Target} DurationMs={DurationMs} DueCount={DueCount}",
            "persistence",
            "failed_actions.get_due",
            "FailedActions",
            queryWatch.ElapsedMilliseconds,
            due.Count);

        var succeeded = 0;
        var rescheduled = 0;
        var dead = 0;

        foreach (var item in due)
        {
            using var itemScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["OperationName"] = "failed_action_retry_item",
                ["CorrelationId"] = item.Pk,
                ["UserId"] = null,
                ["ReminderId"] = null,
                ["SubscriptionId"] = null
            });

            try
            {
                await DispatchAsync(item, ct);
                await _store.MarkSucceededAsync(item.Pk, item.Rk, ct);
                succeeded++;

                _logger.LogInformation(
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts}",
                    "outcome",
                    "completed",
                    "failed_action_retried_successfully",
                    item.ActionType,
                    item.Attempts);
            }
            catch (Exception ex)
            {
                var nextAttempts = item.Attempts + 1;

                if (nextAttempts >= 10)
                {
                    await _store.MarkDeadAsync(item.Pk, item.Rk, nextAttempts, ex.Message, ct);
                    dead++;

                    _logger.LogError(
                        ex,
                        "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts}",
                        "exception",
                        "dependency_failed",
                        "failed_action_marked_dead",
                        item.ActionType,
                        nextAttempts);
                    continue;
                }

                var nextRetryUtc = ComputeBackoffUtc(now, nextAttempts);
                await _store.RescheduleAsync(item.Pk, item.Rk, nextAttempts, nextRetryUtc, ex.Message, ct);
                rescheduled++;

                _logger.LogWarning(
                    ex,
                    "Outcome recorded. LogCategory={LogCategory} Outcome={Outcome} Reason={Reason} ActionType={ActionType} Attempts={Attempts} NextRetryUtc={NextRetryUtc}",
                    "exception",
                    "dependency_failed",
                    "failed_action_rescheduled",
                    item.ActionType,
                    nextAttempts,
                    nextRetryUtc);
            }
        }

        _logger.LogInformation(
            "Operation completed. LogCategory={LogCategory} Outcome={Outcome} Succeeded={Succeeded} Rescheduled={Rescheduled} Dead={Dead} DurationMs={DurationMs}",
            "outcome",
            "completed",
            succeeded,
            rescheduled,
            dead,
            opWatch.ElapsedMilliseconds);
    }

    private async Task DispatchAsync(FailedActionItem item, CancellationToken ct)
    {
        if (item.ActionType == ActionManyChatSync)
        {
            var p = JsonSerializer.Deserialize<ManyChatSyncPayload>(item.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Invalid payload");

            var user = await _users.GetAsync(p.UserPk, p.UserId, ct)
                       ?? throw new InvalidOperationException("User not found for retry");

            if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_retry",
                    "no_action_needed",
                    "missing_subscriber_id");
                return;
            }

            // Build decision from stored snapshot (no recompute needed for retry).
            var snap = user.EffectiveAccess;
            if (snap is null)
            {
                _logger.LogInformation(
                    "Decision recorded. LogCategory={LogCategory} Decision={Decision} Outcome={Outcome} Reason={Reason}",
                    "decision",
                    "dispatch_manychat_retry",
                    "no_action_needed",
                    "missing_access_snapshot");
                return;
            }

            var decision = new AccessDecision(
                snap.Mode,
                snap.Source,
                snap.GraceEndsAtUtc,
                Reason: "Retry ManyChat sync");

            var dependencyWatch = Stopwatch.StartNew();
            await _manyChat.SyncUserAccessAsync(user, decision, ct);

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success}",
                "dependency",
                "manychat",
                "sync_user_access_retry",
                "ManyChat API",
                dependencyWatch.ElapsedMilliseconds,
                true);

            return;
        }

        throw new InvalidOperationException($"Unknown actionType: {item.ActionType}");
    }

    private static DateTimeOffset ComputeBackoffUtc(DateTimeOffset now, int attempts)
    {
        // exponential-ish: 1,2,4,8,16,32,60,120,240...
        var minutes = attempts switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 8,
            5 => 16,
            6 => 32,
            7 => 60,
            8 => 120,
            9 => 240,
            _ => 360
        };
        return now.AddMinutes(minutes);
    }

    private sealed record ManyChatSyncPayload(string UserPk, string UserId, string? Reason);
}

