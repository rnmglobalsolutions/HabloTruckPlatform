using HabloTruckPlatform.Application.Models;
using Azure.Data.Tables;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;
using HabloTruckPlatform.Infrastructure.Storage.Stores;

namespace HabloTruckPlatform.Domain.Tests.Infrastructure;

public sealed class TableStoreBehaviorTests
{
    [Fact]
    public async Task TableSubscriptionReminderStore_Should_BuildDeterministicKeys_AndLowercaseReminderType()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository();
        var sut = new TableSubscriptionReminderStore(factory, repo);

        var periodEnd = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        var sentAt = new DateTimeOffset(2026, 4, 14, 6, 0, 0, TimeSpan.Zero);

        await sut.TryMarkSentAsync(" sub_ABC ", " Renewal_Reminder_1D ", periodEnd, sentAt);

        var insert = Assert.Single(repo.TryInserts);
        Assert.Equal(TableNames.SubscriptionReminders, insert.TableName);

        var entity = Assert.IsType<SubscriptionReminderEntity>(insert.Entity);
        var expectedBucket = Buckets.StableHashMod("sub_ABC", 50);

        Assert.Equal($"{TablePrefixes.SubscriptionReminder}_{expectedBucket:D2}", entity.PartitionKey);
        Assert.Equal("sub_ABC_renewal_reminder_1d_20260415", entity.RowKey);
        Assert.Equal("renewal_reminder_1d", entity.ReminderType);
        Assert.Equal(sentAt, entity.SentAtUtc);
    }

    [Fact]
    public async Task TableSubscriptionReminderStore_Should_ReturnFalse_WhenReminderWindowAlreadyMarkedSent()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository { NextTryInsertResult = false };
        var sut = new TableSubscriptionReminderStore(factory, repo);

        var result = await sut.TryMarkSentAsync(
            "sub_dup",
            "window_7d",
            new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 14, 0, 0, 0, TimeSpan.Zero));

        Assert.False(result);
        Assert.Single(repo.TryInserts);
    }

    [Fact]
    public async Task TableStripeEventStore_Should_UseStripePartitionAndTrimmedRowKey()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository();
        var sut = new TableStripeEventStore(factory, repo);

        await sut.TryMarkProcessedAsync(" evt_123 ", "invoice.paid", new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        var insert = Assert.Single(repo.TryInserts);
        Assert.Equal(TableNames.StripeEvents, insert.TableName);

        var entity = Assert.IsType<StripeEventEntity>(insert.Entity);
        Assert.Equal($"{TablePrefixes.Stripe}_EVT", entity.PartitionKey);
        Assert.Equal("evt_123", entity.RowKey);
        Assert.Equal("invoice.paid", entity.EventType);
    }

    [Fact]
    public async Task TableStripeEventStore_Should_ReturnFalse_When_EventAlreadyProcessed()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository { NextTryInsertResult = false };
        var sut = new TableStripeEventStore(factory, repo);

        var firstTime = await sut.TryMarkProcessedAsync(
            "evt_dup",
            "invoice.paid",
            new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        Assert.False(firstTime);
        Assert.Single(repo.TryInserts);
    }

    [Fact]
    public async Task TableEntitlementStore_CreateAsync_Should_Throw_When_RowAlreadyExists()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository { NextTryInsertResult = false };
        var sut = new TableEntitlementStore(factory, repo);

        var entitlement = new Entitlement
        {
            CompanyId = "C_DUP",
            EntitlementId = "E_DUP",
            SeatsTotal = 10,
            SeatsUsed = 1,
            Status = "active",
            StartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndUtc = null,
            UpdatedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAsync(entitlement));
    }

    [Fact]
    public async Task TableEntitlementStore_SetStatusAsync_Should_UpsertUpdatedStatus_WhenEntitlementExists()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository
        {
            GetOrNullResult = new EntitlementEntity
            {
                PartitionKey = EntitlementMapper.Pk("C_SET"),
                RowKey = "E_SET",
                CompanyId = "C_SET",
                EntitlementId = "E_SET",
                SeatsTotal = 5,
                SeatsUsed = 1,
                Status = "active",
                StartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                EndUtc = null
            }
        };

        var sut = new TableEntitlementStore(factory, repo);

        await sut.SetStatusAsync("C_SET", "E_SET", "expired");

        var upsert = Assert.Single(repo.Upserts);
        var entity = Assert.IsType<EntitlementEntity>(upsert.Entity);
        Assert.Equal("expired", entity.Status);
        Assert.Equal(EntitlementMapper.Pk("C_SET"), entity.PartitionKey);
        Assert.Equal("E_SET", entity.RowKey);
    }

    [Fact]
    public async Task TableEntitlementStore_GetAsync_Should_MapEntityBackToDomainModel()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository();
        repo.GetOrNullResult = new EntitlementEntity
        {
            PartitionKey = EntitlementMapper.Pk("C_GET"),
            RowKey = "E_GET",
            CompanyId = "C_GET",
            EntitlementId = "E_GET",
            SeatsTotal = 25,
            SeatsUsed = 4,
            Status = "active",
            StartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var sut = new TableEntitlementStore(factory, repo);

        var entitlement = await sut.GetAsync("C_GET", "E_GET");

        Assert.NotNull(entitlement);
        Assert.Equal("C_GET", entitlement!.CompanyId);
        Assert.Equal("E_GET", entitlement.EntitlementId);
        Assert.Equal(25, entitlement.SeatsTotal);
        Assert.Equal(4, entitlement.SeatsUsed);
        Assert.Equal("active", entitlement.Status);
    }

    [Fact]
    public async Task TableGraceIndexStore_UpsertAsync_Should_CreateHourBucketAndSortableRowKey()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository();
        var sut = new TableGraceIndexStore(factory, repo);

        var graceEndsAtUtc = new DateTimeOffset(2026, 3, 11, 15, 0, 0, TimeSpan.Zero);
        await sut.UpsertAsync(new UserRef("HT_U_123", "U_GRACE"), graceEndsAtUtc);

        var upsert = Assert.Single(repo.Upserts);
        Assert.Equal(TableNames.GraceIndex, upsert.TableName);

        var entity = Assert.IsType<GraceIndexEntity>(upsert.Entity);
        Assert.Equal("HT_GRACE_2026031115", entity.PartitionKey);
        Assert.Equal($"{graceEndsAtUtc.Ticks:D19}_U_GRACE", entity.RowKey);
        Assert.Equal("HT_U_123", entity.UserPk);
        Assert.Equal("U_GRACE", entity.UserId);
        Assert.Equal(graceEndsAtUtc, entity.GraceEndsAtUtc);
    }

    [Fact]
    public async Task TableSeatAssignmentStore_UpsertAsync_Should_UseCompanyPartitionAndUserRowKey()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository();
        var sut = new TableSeatAssignmentStore(factory, repo);

        var seat = new SeatAssignment
        {
            CompanyId = "C_SEAT",
            UserId = "U_SEAT",
            EntitlementId = "E_SEAT",
            Status = "active",
            AssignedAtUtc = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero)
        };

        await sut.UpsertAsync(seat);

        var upsert = Assert.Single(repo.Upserts);
        Assert.Equal(TableNames.Seats, upsert.TableName);

        var entity = Assert.IsType<SeatAssignmentEntity>(upsert.Entity);
        Assert.Equal(SeatAssignmentMapper.Pk("C_SEAT"), entity.PartitionKey);
        Assert.Equal("U_SEAT", entity.RowKey);
        Assert.Equal("E_SEAT", entity.EntitlementId);
        Assert.Equal("active", entity.Status);
    }

    [Fact]
    public async Task TableSeatAssignmentStore_RevokeAsync_Should_PersistRevokedStatus()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository
        {
            GetOrNullResult = new SeatAssignmentEntity
            {
                PartitionKey = SeatAssignmentMapper.Pk("C_REVOKE"),
                RowKey = "U_REVOKE",
                CompanyId = "C_REVOKE",
                UserId = "U_REVOKE",
                EntitlementId = "E_REVOKE",
                Status = "active",
                AssignedAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)
            }
        };

        var sut = new TableSeatAssignmentStore(factory, repo);

        await sut.RevokeAsync("C_REVOKE", "U_REVOKE");

        var upsert = Assert.Single(repo.Upserts);
        Assert.Equal(TableNames.Seats, upsert.TableName);

        var entity = Assert.IsType<SeatAssignmentEntity>(upsert.Entity);
        Assert.Equal(SeatAssignmentMapper.Pk("C_REVOKE"), entity.PartitionKey);
        Assert.Equal("U_REVOKE", entity.RowKey);
        Assert.Equal("revoked", entity.Status);
    }

    [Fact]
    public async Task TableUserStore_UpsertAsync_Should_UseBucketPartitionKey()
    {
        var factory = new FakeTableClientFactory();
        var repo = new FakeTableRepository();
        var sut = new TableUserStore(factory, repo);

        var user = new User
        {
            UserId = "U_BUCKET_1",
            StripeCustomerId = "cus_bucket_1",
            StripeSubscriptionId = "sub_bucket_1",
            SubscriptionStatus = "active",
            StripeCancelAtPeriodEnd = true,
            StripeCurrentPeriodEndUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)
        };

        await sut.UpsertAsync(user);

        var upsert = Assert.Single(repo.Upserts);
        Assert.Equal(TableNames.Users, upsert.TableName);

        var entity = Assert.IsType<UserEntity>(upsert.Entity);
        Assert.Equal(Buckets.UserBucketPk("U_BUCKET_1"), entity.PartitionKey);
        Assert.Equal("U_BUCKET_1", entity.RowKey);
        Assert.True(entity.StripeCancelAtPeriodEnd);
        Assert.Equal(user.StripeCurrentPeriodEndUtc, entity.StripeCurrentPeriodEndUtc);
    }

    private sealed class FakeTableClientFactory : ITableClientFactory
    {
        private readonly Dictionary<string, TableClient> _clients = new(StringComparer.OrdinalIgnoreCase);

        public TableClient GetClient(string tableName)
        {
            if (!_clients.TryGetValue(tableName, out var client))
            {
                client = new TableClient("UseDevelopmentStorage=true", tableName);
                _clients[tableName] = client;
            }

            return client;
        }

        public Task EnsureTableAsync(string tableName, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeTableRepository : ITableRepository
    {
        public bool NextTryInsertResult { get; set; } = true;
        public object? GetOrNullResult { get; set; }

        public List<TableInsertCall> TryInserts { get; } = new();
        public List<TableUpsertCall> Upserts { get; } = new();

        public Task<T?> GetOrNullAsync<T>(TableClient table, string pk, string rk, CancellationToken ct = default) where T : class, ITableEntity
            => Task.FromResult(GetOrNullResult as T);

        public Task UpsertAsync<T>(TableClient table, T entity, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken ct = default) where T : class, ITableEntity
        {
            Upserts.Add(new TableUpsertCall(table.Name, entity));
            return Task.CompletedTask;
        }

        public Task<bool> DeleteIfExistsAsync(TableClient table, string pk, string rk, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> TryInsertAsync<T>(TableClient table, T entity, CancellationToken ct = default) where T : class, ITableEntity
        {
            TryInserts.Add(new TableInsertCall(table.Name, entity));
            return Task.FromResult(NextTryInsertResult);
        }
    }

    private sealed record TableInsertCall(string TableName, object Entity);
    private sealed record TableUpsertCall(string TableName, object Entity);
}


