using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Integrations.ManyChat;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Abstractions;
using HabloTruckPlatform.Domain.Access;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class CompanyJoinHandlerTests
{
    [Fact]
    public async Task HandleJoinAsync_Should_Succeed_When_SeatsAreAvailable()
    {
        var fixture = BuildFixture();

        var user = new User
        {
            UserId = "U_join_ok",
            ManyChatSubscriberId = "sid_join_ok"
        };

        fixture.UserStore.Add(user);
        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_JOIN_OK",
            EntitlementId = "E_JOIN_OK",
            SeatsTotal = 2,
            SeatsUsed = 0,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        await fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(user.UserId),
            UserId: user.UserId,
            CompanyId: "C_JOIN_OK",
            EntitlementId: "E_JOIN_OK",
            InviteCode: "HT-AAA111"));

        var savedUser = await fixture.UserStore.GetAsync(Buckets.UserBucketPk(user.UserId), user.UserId);
        var seat = await fixture.SeatStore.GetAsync("C_JOIN_OK", user.UserId);
        var entitlement = await fixture.EntitlementStore.GetAsync("C_JOIN_OK", "E_JOIN_OK");

        Assert.NotNull(savedUser);
        Assert.NotNull(seat);
        Assert.NotNull(entitlement);
        Assert.Equal("C_JOIN_OK", savedUser!.CompanyId);
        Assert.Equal("E_JOIN_OK", savedUser.SeatEntitlementId);
        Assert.Equal("active", savedUser.SeatStatus);
        Assert.Equal(1, entitlement!.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_Be_Idempotent_When_UserAlreadyHasActiveSeat()
    {
        var fixture = BuildFixture();

        var user = new User
        {
            UserId = "U_join_repeat",
            CompanyId = "C_REPEAT",
            SeatEntitlementId = "E_REPEAT",
            SeatStatus = "active"
        };

        fixture.UserStore.Add(user);
        await fixture.SeatStore.UpsertAsync(new SeatAssignment
        {
            CompanyId = "C_REPEAT",
            UserId = user.UserId,
            EntitlementId = "E_REPEAT",
            Status = "active",
            AssignedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_REPEAT",
            EntitlementId = "E_REPEAT",
            SeatsTotal = 3,
            SeatsUsed = 0,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        await fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(user.UserId),
            UserId: user.UserId,
            CompanyId: "C_REPEAT",
            EntitlementId: "E_REPEAT",
            InviteCode: "HT-REPEAT1"));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_REPEAT", "E_REPEAT");

        Assert.NotNull(entitlement);
        Assert.Equal(1, entitlement!.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_HealUserProjection_When_SeatAlreadyExists_AndEntitlementIsInactive()
    {
        var fixture = BuildFixture();

        var user = new User
        {
            UserId = "U_join_heal",
            ManyChatSubscriberId = "sid_join_heal"
        };

        fixture.UserStore.Add(user);
        await fixture.SeatStore.UpsertAsync(new SeatAssignment
        {
            CompanyId = "C_HEAL",
            UserId = user.UserId,
            EntitlementId = "E_HEAL",
            Status = "active",
            AssignedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_HEAL",
            EntitlementId = "E_HEAL",
            SeatsTotal = 1,
            SeatsUsed = 0,
            IsOverCapacity = false,
            Status = "past_due",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        await fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(user.UserId),
            UserId: user.UserId,
            CompanyId: "C_HEAL",
            EntitlementId: "E_HEAL",
            InviteCode: "HT-HEAL11"));

        var savedUser = await fixture.UserStore.GetAsync(Buckets.UserBucketPk(user.UserId), user.UserId);
        var entitlement = await fixture.EntitlementStore.GetAsync("C_HEAL", "E_HEAL");

        Assert.NotNull(savedUser);
        Assert.NotNull(entitlement);
        Assert.Equal("C_HEAL", savedUser!.CompanyId);
        Assert.Equal("E_HEAL", savedUser.SeatEntitlementId);
        Assert.Equal("active", savedUser.SeatStatus);
        Assert.Equal(1, entitlement!.SeatsUsed);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_Fail_When_SeatsUsed_Equals_SeatsTotal()
    {
        var fixture = BuildFixture();

        var existingUser = new User
        {
            UserId = "U_existing",
            CompanyId = "C_FULL",
            SeatEntitlementId = "E_FULL",
            SeatStatus = "active"
        };
        fixture.UserStore.Add(existingUser);
        await fixture.SeatStore.UpsertAsync(new SeatAssignment
        {
            CompanyId = "C_FULL",
            UserId = existingUser.UserId,
            EntitlementId = "E_FULL",
            Status = "active",
            AssignedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        var joiningUser = new User
        {
            UserId = "U_join_blocked",
            ManyChatSubscriberId = "sid_join_blocked"
        };
        fixture.UserStore.Add(joiningUser);

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_FULL",
            EntitlementId = "E_FULL",
            SeatsTotal = 1,
            SeatsUsed = 0,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        var ex = await Assert.ThrowsAsync<CompanyJoinException>(() => fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(joiningUser.UserId),
            UserId: joiningUser.UserId,
            CompanyId: "C_FULL",
            EntitlementId: "E_FULL",
            InviteCode: "HT-BBB222")));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_FULL", "E_FULL");
        var seat = await fixture.SeatStore.GetAsync("C_FULL", joiningUser.UserId);

        Assert.Equal("no_seats_available", ex.Code);
        Assert.NotNull(entitlement);
        Assert.Equal(1, entitlement!.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
        Assert.Null(seat);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_Fail_When_EntitlementIsOverCapacity()
    {
        var fixture = BuildFixture();

        var joiningUser = new User
        {
            UserId = "U_join_overcapacity"
        };
        fixture.UserStore.Add(joiningUser);

        for (var i = 0; i < 3; i++)
        {
            await fixture.SeatStore.UpsertAsync(new SeatAssignment
            {
                CompanyId = "C_OVER",
                UserId = $"U_OVER_{i}",
                EntitlementId = "E_OVER",
                Status = "active",
                AssignedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
            });
        }

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_OVER",
            EntitlementId = "E_OVER",
            SeatsTotal = 2,
            SeatsUsed = 3,
            IsOverCapacity = true,
            Status = "active",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        var ex = await Assert.ThrowsAsync<CompanyJoinException>(() => fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(joiningUser.UserId),
            UserId: joiningUser.UserId,
            CompanyId: "C_OVER",
            EntitlementId: "E_OVER",
            InviteCode: "HT-CCC333")));

        Assert.Equal("company_over_capacity", ex.Code);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_Fail_When_EntitlementIsInactive()
    {
        var fixture = BuildFixture();

        var joiningUser = new User
        {
            UserId = "U_join_inactive"
        };
        fixture.UserStore.Add(joiningUser);

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_INACTIVE",
            EntitlementId = "E_INACTIVE",
            SeatsTotal = 5,
            SeatsUsed = 1,
            IsOverCapacity = false,
            Status = "past_due",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        var ex = await Assert.ThrowsAsync<CompanyJoinException>(() => fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(joiningUser.UserId),
            UserId: joiningUser.UserId,
            CompanyId: "C_INACTIVE",
            EntitlementId: "E_INACTIVE",
            InviteCode: "HT-DDD444")));

        Assert.Equal("entitlement_not_active", ex.Code);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_Fail_When_EntitlementHasExpired()
    {
        var fixture = BuildFixture();

        var joiningUser = new User
        {
            UserId = "U_join_expired"
        };
        fixture.UserStore.Add(joiningUser);

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_EXPIRED",
            EntitlementId = "E_EXPIRED",
            SeatsTotal = 5,
            SeatsUsed = 1,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = fixture.Clock.UtcNow.AddDays(-10),
            EndUtc = fixture.Clock.UtcNow.AddMinutes(-1),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        var ex = await Assert.ThrowsAsync<CompanyJoinException>(() => fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(joiningUser.UserId),
            UserId: joiningUser.UserId,
            CompanyId: "C_EXPIRED",
            EntitlementId: "E_EXPIRED",
            InviteCode: "HT-EXP555")));

        Assert.Equal("entitlement_not_active", ex.Code);
    }

    [Fact]
    public async Task HandleJoinAsync_Should_ReleaseReservation_When_SeatActivationFails()
    {
        var seatStore = new ConflictSeatStore();
        var fixture = BuildFixture(seatStore);

        var joiningUser = new User
        {
            UserId = "U_join_conflict"
        };
        fixture.UserStore.Add(joiningUser);

        await fixture.EntitlementStore.UpsertAsync(new Entitlement
        {
            CompanyId = "C_CONFLICT",
            EntitlementId = "E_CONFLICT",
            SeatsTotal = 2,
            SeatsUsed = 0,
            IsOverCapacity = false,
            Status = "active",
            StartUtc = fixture.Clock.UtcNow.AddDays(-2),
            UpdatedAtUtc = fixture.Clock.UtcNow.AddDays(-1)
        });

        var ex = await Assert.ThrowsAsync<CompanyJoinException>(() => fixture.Handler.HandleJoinAsync(new JoinCompanyRequest(
            UserPk: Buckets.UserBucketPk(joiningUser.UserId),
            UserId: joiningUser.UserId,
            CompanyId: "C_CONFLICT",
            EntitlementId: "E_CONFLICT",
            InviteCode: "HT-CONFLICT")));

        var entitlement = await fixture.EntitlementStore.GetAsync("C_CONFLICT", "E_CONFLICT");
        var seat = await fixture.SeatStore.GetAsync("C_CONFLICT", joiningUser.UserId);

        Assert.Equal("seat_assignment_conflict", ex.Code);
        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement!.SeatsUsed);
        Assert.False(entitlement.IsOverCapacity);
        Assert.Null(seat);
    }

    private static Fixture BuildFixture(ISeatAssignmentStore? seatStore = null)
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var userStore = new InMemoryUserStore();
        var entitlementStore = new InMemoryEntitlementStore();
        seatStore ??= new InMemorySeatStore();
        var failedActionStore = new NoopFailedActionStore();
        var manyChatSync = new NoopManyChatSync();

        var orchestrator = new AccessOrchestrator(
            userStore,
            seatStore,
            entitlementStore,
            manyChatSync,
            failedActionStore,
            clock,
            new CompanyGracePolicy(7),
            NullLogger<AccessOrchestrator>.Instance);

        var handler = new CompanyJoinHandler(
            userStore,
            entitlementStore,
            seatStore,
            orchestrator,
            clock,
            NullLogger<CompanyJoinHandler>.Instance);

        return new Fixture(handler, userStore, entitlementStore, seatStore, clock);
    }

    private sealed record Fixture(
        CompanyJoinHandler Handler,
        InMemoryUserStore UserStore,
        InMemoryEntitlementStore EntitlementStore,
        ISeatAssignmentStore SeatStore,
        FixedClock Clock);

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class InMemoryUserStore : IUserStore
    {
        private readonly Dictionary<(string Pk, string Id), User> _rows = new();

        public void Add(User user)
            => _rows[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;

        public Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((userPk, userId), out var user) ? user : null);

        public Task UpsertAsync(User user, CancellationToken ct = default)
        {
            _rows[(Buckets.UserBucketPk(user.UserId), user.UserId)] = user;
            return Task.CompletedTask;
        }

        public Task<User> GetOrCreateAsync(string? emailNormalized, string? manyChatSubscriberId, string? phoneE164, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task UpsertLookupsAsync(User user, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(int take = 500, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<User>>(Array.Empty<User>());

        public async Task<HabloTruckPlatform.Application.Models.StripeUserScanPage> QueryUsersWithStripePageAsync(int take = 500, int startBucket = 0, CancellationToken ct = default)
        {
            var users = await QueryUsersWithStripeAsync(take, ct);
            return new HabloTruckPlatform.Application.Models.StripeUserScanPage(users, startBucket, startBucket, 0, false);
        }
    }

    private sealed class InMemoryEntitlementStore : IEntitlementStore
    {
        private readonly Dictionary<(string CompanyId, string EntitlementId), Entitlement> _rows = new();

        public Task<Entitlement?> GetAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, entitlementId), out var entitlement) ? entitlement : null);

        public Task CreateAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task UpsertAsync(Entitlement entitlement, CancellationToken ct = default)
        {
            _rows[(entitlement.CompanyId, entitlement.EntitlementId)] = entitlement;
            return Task.CompletedTask;
        }

        public Task SetStatusAsync(string companyId, string entitlementId, string status, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((companyId, entitlementId), out var entitlement))
            {
                entitlement.Status = status;
                _rows[(companyId, entitlementId)] = entitlement;
            }

            return Task.CompletedTask;
        }

        public Task<SeatReservationResult> TryReserveSeatAsync(string companyId, string entitlementId, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue((companyId, entitlementId), out var entitlement))
                return Task.FromResult(new SeatReservationResult(SeatReservationOutcome.NotFound, null, "entitlement_not_found"));

            if (!string.Equals(entitlement.Status, "active", StringComparison.OrdinalIgnoreCase)
                || (entitlement.EndUtc is not null && entitlement.EndUtc <= DateTimeOffset.UtcNow))
            {
                return Task.FromResult(new SeatReservationResult(SeatReservationOutcome.Inactive, entitlement, "entitlement_not_active"));
            }

            entitlement.SeatsUsed = Math.Max(entitlement.SeatsUsed, 0);
            entitlement.IsOverCapacity = entitlement.SeatsUsed > entitlement.SeatsTotal;

            if (entitlement.IsOverCapacity)
                return Task.FromResult(new SeatReservationResult(SeatReservationOutcome.OverCapacity, entitlement, "company_over_capacity"));

            if (entitlement.SeatsUsed >= entitlement.SeatsTotal)
                return Task.FromResult(new SeatReservationResult(SeatReservationOutcome.NoCapacity, entitlement, "no_seats_available"));

            entitlement.SeatsUsed++;
            entitlement.IsOverCapacity = entitlement.SeatsUsed > entitlement.SeatsTotal;
            _rows[(companyId, entitlementId)] = entitlement;
            return Task.FromResult(new SeatReservationResult(SeatReservationOutcome.Reserved, entitlement));
        }

        public Task<Entitlement?> SyncSeatsUsedAsync(string companyId, string entitlementId, int seatsUsedFloor, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((companyId, entitlementId), out var entitlement))
            {
                entitlement.SeatsUsed = Math.Max(entitlement.SeatsUsed, Math.Max(seatsUsedFloor, 0));
                entitlement.IsOverCapacity = entitlement.SeatsUsed > entitlement.SeatsTotal;
                _rows[(companyId, entitlementId)] = entitlement;
                return Task.FromResult<Entitlement?>(entitlement);
            }

            return Task.FromResult<Entitlement?>(null);
        }

        public Task ReleaseSeatReservationAsync(string companyId, string entitlementId, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((companyId, entitlementId), out var entitlement))
            {
                if (entitlement.SeatsUsed > 0)
                    entitlement.SeatsUsed--;

                entitlement.IsOverCapacity = entitlement.SeatsUsed > entitlement.SeatsTotal;
                _rows[(companyId, entitlementId)] = entitlement;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySeatStore : ISeatAssignmentStore
    {
        private readonly Dictionary<(string CompanyId, string UserId), SeatAssignment> _rows = new();

        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue((companyId, userId), out var seat) ? seat : null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
        {
            seat.UpdatedAtUtc = seat.AssignedAtUtc == default ? DateTimeOffset.UtcNow : seat.UpdatedAtUtc;
            _rows[(seat.CompanyId, seat.UserId)] = seat;
            return Task.CompletedTask;
        }

        public Task<SeatActivationResult> EnsureActiveAsync(SeatAssignment seat, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((seat.CompanyId, seat.UserId), out var existing)
                && existing.IsActive()
                && string.Equals(existing.EntitlementId, seat.EntitlementId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new SeatActivationResult(SeatActivationOutcome.AlreadyActive));
            }

            seat.Status = "active";
            seat.RevokedAtUtc = null;
            seat.UpdatedAtUtc = seat.AssignedAtUtc == default ? DateTimeOffset.UtcNow : seat.AssignedAtUtc;
            _rows[(seat.CompanyId, seat.UserId)] = seat;
            return Task.FromResult(new SeatActivationResult(SeatActivationOutcome.Activated));
        }

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
        {
            if (_rows.TryGetValue((companyId, userId), out var seat))
            {
                seat.Status = "revoked";
                seat.RevokedAtUtc = DateTimeOffset.UtcNow;
                _rows[(companyId, userId)] = seat;
            }

            return Task.CompletedTask;
        }

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(_rows.Values.Count(x =>
                string.Equals(x.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.EntitlementId, entitlementId, StringComparison.OrdinalIgnoreCase)
                && x.IsActive()));
    }

    private sealed class ConflictSeatStore : ISeatAssignmentStore
    {
        public Task<SeatAssignment?> GetAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.FromResult<SeatAssignment?>(null);

        public Task UpsertAsync(SeatAssignment seat, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<SeatActivationResult> EnsureActiveAsync(SeatAssignment seat, CancellationToken ct = default)
            => Task.FromResult(new SeatActivationResult(SeatActivationOutcome.Conflict, "seat_assignment_conflict"));

        public Task RevokeAsync(string companyId, string userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> CountActiveSeatsAsync(string companyId, string entitlementId, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private sealed class NoopFailedActionStore : IFailedActionStore
    {
        public Task EnsureTableAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task EnqueueAsync(string actionType, string payloadJson, DateTimeOffset nextRetryUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FailedActionItem>> GetDueAsync(DateTimeOffset nowUtc, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());
        public Task MarkSucceededAsync(string pk, string rk, CancellationToken ct = default) => Task.CompletedTask;
        public Task RescheduleAsync(string pk, string rk, int attempts, DateTimeOffset nextRetryUtc, string lastError, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkDeadAsync(string pk, string rk, int attempts, string lastError, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FailedActionItem>> GetByStatusAsync(DateTimeOffset nowUtc, string status, int lookbackHours, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FailedActionItem>>(Array.Empty<FailedActionItem>());
        public Task RequeueAsync(string pk, string rk, DateTimeOffset nextRetryUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopManyChatSync : IManyChatSync
    {
        public Task SyncUserAccessAsync(User user, AccessDecision decision, CancellationToken ct = default) => Task.CompletedTask;
        public Task TriggerPaymentFailedFlowAsync(string subscriberId, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCompanyPackPurchasedAsync(string companyId, int seatsTotal, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendSubscriptionReminderAsync(SubscriptionReminderDispatch dispatch, CancellationToken ct = default) => Task.CompletedTask;
        public Task SyncBillingRecoveryStatusAsync(BillingRecoveryManyChatUpdate update, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ManyChatResponse> RemoveTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default) => Task.FromResult(new ManyChatResponse());
        public Task<ManyChatResponse> AddTagByNameAsync(string subscriberId, string tagName, CancellationToken ct = default) => Task.FromResult(new ManyChatResponse());
        public Task<ManyChatResponse> SetCustomFieldByNameAsync(string subscriberId, string fieldName, string value, CancellationToken ct = default) => Task.FromResult(new ManyChatResponse());
    }
}
