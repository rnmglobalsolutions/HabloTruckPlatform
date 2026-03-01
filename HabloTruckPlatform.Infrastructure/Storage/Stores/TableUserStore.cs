using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableUserStore : IUserStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableUserStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient UsersTable => _factory.GetClient(TableNames.Users);
    private TableClient EmailTable => _factory.GetClient(TableNames.UserEmail);
    private TableClient ManyChatTable => _factory.GetClient(TableNames.UserManyChat);
    private TableClient StripeCustomerTable => _factory.GetClient(TableNames.UserStripeCustomer);

    public async Task EnsureTablesAsync(CancellationToken ct = default)
    {
        await _factory.EnsureTableAsync(TableNames.Users, ct);
        await _factory.EnsureTableAsync(TableNames.UserEmail, ct);
        await _factory.EnsureTableAsync(TableNames.UserManyChat, ct);
        await _factory.EnsureTableAsync(TableNames.UserStripeCustomer, ct);
    }

    public async Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userPk) || string.IsNullOrWhiteSpace(userId))
            return null;

        var entity = await _repo.GetOrNullAsync<UserEntity>(UsersTable, userPk, userId, ct);
        return entity is null ? null : UserMapper.FromEntity(entity);
    }

    public async Task UpsertAsync(User user, CancellationToken ct = default)
    {
        if (user is null) throw new ArgumentNullException(nameof(user));
        if (string.IsNullOrWhiteSpace(user.UserId)) throw new ArgumentException("UserId is required.", nameof(user));

        var pk = Buckets.UserBucketPk(user.UserId);
        var entity = UserMapper.ToEntity(user, pk);

        await _repo.UpsertAsync(UsersTable, entity, TableUpdateMode.Replace, ct);
    }

    /// <summary>
    /// Creates user if not exists. Uses lookups to prevent duplicates:
    /// - emailNormalized (preferred)
    /// - manyChatSubscriberId
    /// If neither provided, creates a new user always.
    /// </summary>
    public async Task<User> GetOrCreateAsync(
        string? emailNormalized,
        string? manyChatSubscriberId,
        string? phoneE164,
        CancellationToken ct = default)
    {
        emailNormalized = NormalizeEmail(emailNormalized);

        // 1) Try resolve existing by email lookup
        if (!string.IsNullOrWhiteSpace(emailNormalized))
        {
            var existing = await TryGetByEmailAsync(emailNormalized!, ct);
            if (existing is not null) return existing;
        }

        // 2) Try resolve existing by ManyChat subscriber lookup
        if (!string.IsNullOrWhiteSpace(manyChatSubscriberId))
        {
            var existing = await TryGetByManyChatAsync(manyChatSubscriberId!, ct);
            if (existing is not null) return existing;
        }

        // 3) Create new user
        var userId = UlidIds.NewUserId();
        var userPk = Buckets.UserBucketPk(userId);

        var user = new User
        {
            UserId = userId,
            EmailNormalized = emailNormalized,
            ManyChatSubscriberId = manyChatSubscriberId,
            PhoneE164 = phoneE164
        };

        // Insert-only for the user row
        // If this ever conflicts (extremely unlikely), generate another ULID and retry.
        // Insert-only for the user row
        var inserted = await _repo.TryInsertAsync(UsersTable, UserMapper.ToEntity(user, userPk), ct);

        if (!inserted)
        {
            // ultra-rare: retry once with a new ULID (create a NEW User because UserId is init-only)
            var retryUserId = UlidIds.NewUserId();
            var retryUserPk = Buckets.UserBucketPk(retryUserId);

            var retryUser = new User
            {
                UserId = retryUserId,
                EmailNormalized = user.EmailNormalized,
                ManyChatSubscriberId = user.ManyChatSubscriberId,
                PhoneE164 = user.PhoneE164
            };

            inserted = await _repo.TryInsertAsync(UsersTable, UserMapper.ToEntity(retryUser, retryUserPk), ct);
            if (!inserted)
                throw new InvalidOperationException("Unable to create user (ULID collision).");

            user = retryUser;     // keep returning the actual inserted user
            userId = retryUserId; // if you still keep local vars
            userPk = retryUserPk;
        }

        // Create lookups (best effort). If conflict, prefer existing.
        try
        {
            await UpsertLookupsAsync(user, ct);
        }
        catch
        {
            // Worst case: lookups failed due to conflict. Try to resolve again.
            if (!string.IsNullOrWhiteSpace(emailNormalized))
            {
                var resolved = await TryGetByEmailAsync(emailNormalized!, ct);
                if (resolved is not null) return resolved;
            }
            if (!string.IsNullOrWhiteSpace(manyChatSubscriberId))
            {
                var resolved = await TryGetByManyChatAsync(manyChatSubscriberId!, ct);
                if (resolved is not null) return resolved;
            }
            // If still not resolved, return the created one.
        }

        return user;
    }

    public async Task UpsertLookupsAsync(User user, CancellationToken ct = default)
    {
        var userPk = Buckets.UserBucketPk(user.UserId);

        // ---------------------------
        // EMAIL LOOKUP (insert-only)
        // ---------------------------
        if (!string.IsNullOrWhiteSpace(user.EmailNormalized))
        {
            var pk = Buckets.EmailLookupPk(user.EmailNormalized!);
            var rk = user.EmailNormalized!;

            var entity = new UserEmailLookupEntity
            {
                PartitionKey = pk,
                RowKey = rk,
                UserPk = userPk,
                UserId = user.UserId
            };

            try
            {
                await EmailTable.AddEntityAsync(entity, ct); // insert-only
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                // lookup already exists → DO NOT overwrite
                // optional: verify it points to same user
                var existing = await EmailTable.GetEntityAsync<UserEmailLookupEntity>(pk, rk, cancellationToken: ct);

                if (existing.Value.UserId != user.UserId)
                {
                    // another user owns this email → do nothing (security)
                    // caller should resolve existing user instead of linking
                }
            }
        }

        // ---------------------------
        // MANYCHAT LOOKUP (insert-only)
        // ---------------------------
        if (!string.IsNullOrWhiteSpace(user.ManyChatSubscriberId))
        {
            var pk = Buckets.ManyChatLookupPk(user.ManyChatSubscriberId!);
            var rk = user.ManyChatSubscriberId!;

            var entity = new UserManyChatLookupEntity
            {
                PartitionKey = pk,
                RowKey = rk,
                UserPk = userPk,
                UserId = user.UserId
            };

            try
            {
                await ManyChatTable.AddEntityAsync(entity, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                var existing = await ManyChatTable.GetEntityAsync<UserManyChatLookupEntity>(pk, rk, cancellationToken: ct);

                if (existing.Value.UserId != user.UserId)
                {
                    // another user owns this subscriberId → ignore
                }
            }
        }

        // ---------------------------
        // STRIPE CUSTOMER LOOKUP (insert-only)
        // ---------------------------
        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
        {
            var pk = Buckets.StripeCustomerLookupPk(user.StripeCustomerId!);
            var rk = user.StripeCustomerId!;

            var entity = new UserStripeCustomerLookupEntity
            {
                PartitionKey = pk,
                RowKey = rk,
                UserPk = userPk,
                UserId = user.UserId
            };

            try
            {
                await StripeCustomerTable.AddEntityAsync(entity, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                var existing = await StripeCustomerTable.GetEntityAsync<UserStripeCustomerLookupEntity>(pk, rk, cancellationToken: ct);

                if (existing.Value.UserId != user.UserId)
                {
                    // another user owns this customerId → ignore
                }
            }
        }
    }

    // ---------------------------
    // Private lookup resolvers used by GetOrCreate
    // ---------------------------

    private async Task<User?> TryGetByEmailAsync(string emailNormalized, CancellationToken ct)
    {
        var email = NormalizeEmail(emailNormalized);
        if (string.IsNullOrWhiteSpace(email)) return null;

        var pk = Buckets.EmailLookupPk(email);
        var rk = email;

        var lookup = await _repo.GetOrNullAsync<UserEmailLookupEntity>(EmailTable, pk, rk, ct);
        if (lookup is null) return null;

        return await GetAsync(lookup.UserPk, lookup.UserId, ct);
    }

    private async Task<User?> TryGetByManyChatAsync(string subscriberId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subscriberId)) return null;

        var sid = subscriberId.Trim();

        var pk = Buckets.ManyChatLookupPk(sid);
        var rk = sid;

        var lookup = await _repo.GetOrNullAsync<UserManyChatLookupEntity>(ManyChatTable, pk, rk, ct);
        if (lookup is null) return null;

        return await GetAsync(lookup.UserPk, lookup.UserId, ct);
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        return email.Trim().ToLowerInvariant();
    }
}