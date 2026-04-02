using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class StorageInitializer
{
    private readonly ITableClientFactory _factory;
    private readonly ILogger<StorageInitializer> _logger;

    public StorageInitializer(
        ITableClientFactory factory,
        ILogger<StorageInitializer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StorageInitializer starting...");

        // -----------------------------
        // USERS (main + lookup tables)
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.Users, ct);
        await _factory.EnsureTableAsync(TableNames.UserEmail, ct);
        await _factory.EnsureTableAsync(TableNames.UserManyChat, ct);
        await _factory.EnsureTableAsync(TableNames.UserStripeCustomer, ct);

        // -----------------------------
        // GRACE INDEX
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.GraceIndex, ct);

        // -----------------------------
        // STRIPE IDEMPOTENCY
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.StripeEvents, ct);

        // -----------------------------
        // COMPANIES / ENTITLEMENTS
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.Companies, ct);
        await _factory.EnsureTableAsync(TableNames.Entitlements, ct);
        await _factory.EnsureTableAsync(TableNames.EntitlementExpiryIndex, ct);

        // -----------------------------
        // SEATS
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.Seats, ct);

        // -----------------------------
        // INVITES
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.InviteCodes, ct);
        await _factory.EnsureTableAsync(TableNames.InviteCompanyIndex, ct);

        // -----------------------------
        // FAILED ACTIONS (outbox / retry)
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.FailedActions, ct);

        // -----------------------------
        // STRIPE EVENT AUDIT (for observability, debugging, and future analytics)
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.StripeEventAudit, ct);

        // -----------------------------
        // REMINDER IDEMPOTENCY / AUDIT
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.SubscriptionReminders, ct);

        // -----------------------------
        // JOB CHECKPOINTS
        // -----------------------------
        await _factory.EnsureTableAsync(TableNames.JobCheckpoints, ct);

        _logger.LogInformation("StorageInitializer completed.");
    }
}
