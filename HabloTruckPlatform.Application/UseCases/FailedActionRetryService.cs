using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Access;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class FailedActionRetryService
{
    public const string ActionManyChatSync = "manychat.sync";

    private readonly IFailedActionStore _store;
    private readonly IUserStore _users;
    private readonly IManyChatSync _manyChat;

    public FailedActionRetryService(IFailedActionStore store, IUserStore users, IManyChatSync manyChat)
    {
        _store = store;
        _users = users;
        _manyChat = manyChat;
    }

    public async Task RetryDueAsync(int lookbackHours, int take, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var due = await _store.GetDueAsync(now, lookbackHours: lookbackHours, take: take, ct);

        foreach (var item in due)
        {
            try
            {
                await DispatchAsync(item, ct);
                await _store.MarkSucceededAsync(item.Pk, item.Rk, ct);
            }
            catch (Exception ex)
            {
                var nextAttempts = item.Attempts + 1;

                if (nextAttempts >= 10)
                {
                    await _store.MarkDeadAsync(item.Pk, item.Rk, nextAttempts, ex.Message, ct);
                    continue;
                }

                var nextRetryUtc = ComputeBackoffUtc(now, nextAttempts);
                await _store.RescheduleAsync(item.Pk, item.Rk, nextAttempts, nextRetryUtc, ex.Message, ct);
            }
        }
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
                return; // nothing to do

            // Build decision from stored snapshot (no recompute needed for retry)
            var snap = user.EffectiveAccess;
            if (snap is null)
                return;

            var decision = new AccessDecision(
                snap.Mode,
                snap.Source,
                snap.GraceEndsAtUtc,
                Reason: "Retry ManyChat sync");

            await _manyChat.SyncUserAccessAsync(user, decision, ct);
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