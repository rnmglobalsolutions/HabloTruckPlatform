using System.Diagnostics;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class BillingRecoveryManyChatNotifier
{
    private readonly IManyChatSync _manyChat;
    private readonly IManyChatDispatchQueue? _manyChatDispatchQueue;
    private readonly IFailedActionStore _failedActionStore;
    private readonly IClock _clock;
    private readonly IAppMetrics? _metrics;
    private readonly ILogger<BillingRecoveryManyChatNotifier> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public BillingRecoveryManyChatNotifier(
        IManyChatSync manyChat,
        IFailedActionStore failedActionStore,
        IClock clock,
        ILogger<BillingRecoveryManyChatNotifier>? logger = null,
        IManyChatDispatchQueue? manyChatDispatchQueue = null,
        IAppMetrics? metrics = null)
    {
        _manyChat = manyChat;
        _manyChatDispatchQueue = manyChatDispatchQueue;
        _failedActionStore = failedActionStore;
        _clock = clock;
        _metrics = metrics;
        _logger = logger ?? NullLogger<BillingRecoveryManyChatNotifier>.Instance;
    }

    public Task NotifyRecoveryActiveAsync(
        User user,
        string? correlationId,
        CancellationToken ct = default)
        => NotifyAsync(
            BuildUpdate(
                user,
                BillingRecoveryManyChatStatuses.RecoveryActive,
                statusAtUtc: _clock.UtcNow,
                recoveryStartedAtUtc: user.PaymentRecoveryStartedAtUtc,
                invoiceId: null,
                invoiceStatus: null,
                actionRequired: true,
                recovered: false),
            correlationId,
            "recovery_active",
            "manychat_billing_recovery_active",
            ct);

    public Task NotifyRetryOutcomeAsync(
        User user,
        StripeOpenInvoiceRetryAttempt attempt,
        string? correlationId,
        CancellationToken ct = default)
    {
        var status = attempt.InvoiceFound && !attempt.InvoicePaid
            ? BillingRecoveryManyChatStatuses.PaymentRetryFailed
            : BillingRecoveryManyChatStatuses.PaymentUpdatePendingConfirmation;

        var actionRequired = status == BillingRecoveryManyChatStatuses.PaymentRetryFailed;

        return NotifyAsync(
            BuildUpdate(
                user,
                status,
                statusAtUtc: _clock.UtcNow,
                recoveryStartedAtUtc: user.PaymentRecoveryStartedAtUtc,
                invoiceId: attempt.InvoiceId,
                invoiceStatus: attempt.InvoiceStatus,
                actionRequired: actionRequired,
                recovered: false),
            correlationId,
            "retry_outcome",
            "manychat_billing_recovery_retry_outcome",
            ct);
    }

    public Task NotifyRecoveredAsync(
        User user,
        DateTimeOffset? recoveryStartedAtUtc,
        string? correlationId,
        CancellationToken ct = default)
        => NotifyAsync(
            BuildUpdate(
                user,
                BillingRecoveryManyChatStatuses.Recovered,
                statusAtUtc: _clock.UtcNow,
                recoveryStartedAtUtc: recoveryStartedAtUtc,
                invoiceId: null,
                invoiceStatus: "paid",
                actionRequired: false,
                recovered: true),
            correlationId,
            "recovered",
            "manychat_billing_recovery_recovered",
            ct);

    public Task NotifyClosedAsync(
        User user,
        DateTimeOffset? recoveryStartedAtUtc,
        string? correlationId,
        CancellationToken ct = default)
        => NotifyAsync(
            BuildUpdate(
                user,
                BillingRecoveryManyChatStatuses.Closed,
                statusAtUtc: _clock.UtcNow,
                recoveryStartedAtUtc: recoveryStartedAtUtc,
                invoiceId: null,
                invoiceStatus: null,
                actionRequired: false,
                recovered: false),
            correlationId,
            "closed",
            "manychat_billing_recovery_closed",
            ct);

    private async Task NotifyAsync(
        BillingRecoveryManyChatUpdate? update,
        string? correlationId,
        string reason,
        string operationName,
        CancellationToken ct)
    {
        if (update is null || string.IsNullOrWhiteSpace(update.SubscriberId))
            return;

        var watch = Stopwatch.StartNew();
        var payload = JsonSerializer.Serialize(
            new ManyChatBillingRecoveryStateFailedActionPayload(
                Update: update,
                UserPk: Buckets.UserBucketPk(update.UserId),
                UserId: update.UserId,
                CorrelationId: correlationId,
                Reason: reason,
                OperationName: operationName),
            JsonOpts);

        try
        {
            if (_manyChatDispatchQueue is not null)
            {
                await _manyChatDispatchQueue.EnqueueAsync(
                    new ManyChatDispatchMessage(
                        FailedActionRetryService.ActionManyChatBillingRecoveryState,
                        payload,
                        correlationId,
                        _clock.UtcNow),
                    ct);
                _metrics?.ManyChatDispatchQueued(FailedActionRetryService.ActionManyChatBillingRecoveryState);
            }
            else
            {
                await _manyChat.SyncBillingRecoveryStatusAsync(update, ct);
            }

            _logger.LogDebug(
                "Dependency completed. LogCategory={LogCategory} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Target={Target} DurationMs={DurationMs} Success={Success} Status={Status} DispatchMode={DispatchMode}",
                "dependency",
                _manyChatDispatchQueue is null ? "manychat" : "manychat_dispatch_queue",
                _manyChatDispatchQueue is null ? "sync_billing_recovery_status" : "enqueue_billing_recovery_status",
                _manyChatDispatchQueue is null ? "ManyChat API" : "Azure Queue Storage",
                watch.ElapsedMilliseconds,
                true,
                update.Status,
                _manyChatDispatchQueue is null ? "direct" : "queue");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ManyChatRequestException ex) when (ex.IsRetryable)
        {
            _logger.LogWarning(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Status={Status} DurationMs={DurationMs} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                "exception",
                "dependency_failed",
                "manychat",
                "sync_billing_recovery_status",
                update.Status,
                watch.ElapsedMilliseconds,
                ex.IsRetryable,
                ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                ex.FailureCategory);

            await _failedActionStore.EnqueueAsync(
                FailedActionRetryService.ActionManyChatBillingRecoveryState,
                payload,
                _clock.UtcNow.AddMinutes(2),
                ct);
        }
        catch (ManyChatRequestException ex)
        {
            _logger.LogError(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Status={Status} DurationMs={DurationMs} IsRetryable={IsRetryable} StatusCode={StatusCode} FailureCategory={FailureCategory}",
                "exception",
                "validation_failed",
                "manychat",
                "sync_billing_recovery_status",
                update.Status,
                watch.ElapsedMilliseconds,
                ex.IsRetryable,
                ex.StatusCode is null ? null : (int)ex.StatusCode.Value,
                ex.FailureCategory);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Dependency failed. LogCategory={LogCategory} Outcome={Outcome} DependencyType={DependencyType} DependencyOperation={DependencyOperation} Status={Status} DurationMs={DurationMs}",
                "exception",
                "dependency_failed",
                "manychat",
                "sync_billing_recovery_status",
                update.Status,
                watch.ElapsedMilliseconds);

            if (_manyChatDispatchQueue is not null)
            {
                try
                {
                    await _failedActionStore.EnqueueAsync(
                        FailedActionRetryService.ActionManyChatBillingRecoveryState,
                        payload,
                        _clock.UtcNow.AddMinutes(2),
                        ct);
                }
                catch (Exception enqueueEx)
                {
                    _logger.LogError(
                        enqueueEx,
                        "Persistence failed. LogCategory={LogCategory} Outcome={Outcome} PersistenceOperation={PersistenceOperation} Target={Target} Status={Status}",
                        "exception",
                        "dependency_failed",
                        "failed_action.enqueue",
                        "FailedActions",
                        update.Status);
                }
            }
        }
    }

    private static BillingRecoveryManyChatUpdate? BuildUpdate(
        User user,
        string status,
        DateTimeOffset statusAtUtc,
        DateTimeOffset? recoveryStartedAtUtc,
        string? invoiceId,
        string? invoiceStatus,
        bool actionRequired,
        bool recovered)
    {
        if (string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
            return null;

        return new BillingRecoveryManyChatUpdate(
            SubscriberId: user.ManyChatSubscriberId!.Trim(),
            UserId: user.UserId,
            CompanyId: user.CompanyId,
            SubscriptionId: user.StripeSubscriptionId,
            Status: status,
            StatusAtUtc: statusAtUtc,
            RecoveryStartedAtUtc: recoveryStartedAtUtc,
            InvoiceId: invoiceId,
            InvoiceStatus: invoiceStatus,
            ActionRequired: actionRequired,
            Recovered: recovered);
    }
}
