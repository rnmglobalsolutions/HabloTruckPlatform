using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableSubscriptionReminderStore : ISubscriptionReminderStore
{
    private const int BucketCount = 50;

    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableSubscriptionReminderStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient Table => _factory.GetClient(TableNames.SubscriptionReminders);

    public Task<bool> TryMarkSentAsync(
        string subscriptionId,
        string reminderType,
        DateTimeOffset periodEndUtc,
        DateTimeOffset sentAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriptionId))
            throw new ArgumentException("subscriptionId is required.", nameof(subscriptionId));

        if (string.IsNullOrWhiteSpace(reminderType))
            throw new ArgumentException("reminderType is required.", nameof(reminderType));

        var subId = subscriptionId.Trim();
        var type = reminderType.Trim().ToLowerInvariant();
        var period = periodEndUtc.ToUniversalTime();

        var bucket = Buckets.StableHashMod(subId, BucketCount);
        var pk = $"{TablePrefixes.SubscriptionReminder}_{bucket:D2}";
        var rk = $"{subId}_{type}_{period:yyyyMMdd}";

        var entity = new SubscriptionReminderEntity
        {
            PartitionKey = pk,
            RowKey = rk,
            SubscriptionId = subId,
            ReminderType = type,
            PeriodEndUtc = period,
            SentAtUtc = sentAtUtc.ToUniversalTime()
        };

        return _repo.TryInsertAsync(Table, entity, ct);
    }
}
