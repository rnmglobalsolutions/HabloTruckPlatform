using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableUserStore : IUserStore
{
    private readonly TableClient _users;
    private readonly TableClient _email;
    private readonly TableClient _manyChat;
    private readonly TableClient _stripeCustomer;

    public TableUserStore(TableServiceClient serviceClient)
    {
        _users = serviceClient.GetTableClient(TableNames.Users);
        _email = serviceClient.GetTableClient(TableNames.UserEmail);
        _manyChat = serviceClient.GetTableClient(TableNames.UserManyChat);
        _stripeCustomer = serviceClient.GetTableClient(TableNames.UserStripeCustomer);
    }

    public async Task EnsureTablesAsync(CancellationToken ct = default)
    {
        await _users.CreateIfNotExistsAsync(ct);
        await _email.CreateIfNotExistsAsync(ct);
        await _manyChat.CreateIfNotExistsAsync(ct);
        await _stripeCustomer.CreateIfNotExistsAsync(ct);
    }

    public async Task<User?> GetAsync(string userPk, string userId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _users.GetEntityAsync<UserEntity>(userPk, userId, cancellationToken: ct);
            return UserMapper.FromEntity(resp.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertAsync(User user, CancellationToken ct = default)
    {
        var pk = Buckets.UserBucketPk(user.UserId);
        var entity = UserMapper.ToEntity(user, pk);
        await _users.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
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

        // Persist user first
        await _users.AddEntityAsync(UserMapper.ToEntity(user, userPk), ct);

        // Create lookups (insert-only where possible; if conflict, prefer existing)
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

        // Email lookup
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

            // Insert-or-replace is OK here; if you want strict uniqueness, use AddEntity + catch 409.
            await _email.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }

        // ManyChat lookup
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

            await _manyChat.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }

        // Stripe customer lookup
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

            await _stripeCustomer.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }
    }

    // ---------------------------
    // Private lookup resolvers used by GetOrCreate
    // ---------------------------

    private async Task<User?> TryGetByEmailAsync(string emailNormalized, CancellationToken ct)
    {
        try
        {
            var pk = Buckets.EmailLookupPk(emailNormalized);
            var rk = emailNormalized;

            var lookup = await _email.GetEntityAsync<UserEmailLookupEntity>(pk, rk, cancellationToken: ct);
            return await GetAsync(lookup.Value.UserPk, lookup.Value.UserId, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<User?> TryGetByManyChatAsync(string subscriberId, CancellationToken ct)
    {
        try
        {
            var pk = Buckets.ManyChatLookupPk(subscriberId);
            var rk = subscriberId;

            var lookup = await _manyChat.GetEntityAsync<UserManyChatLookupEntity>(pk, rk, cancellationToken: ct);
            return await GetAsync(lookup.Value.UserPk, lookup.Value.UserId, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        return email.Trim().ToLowerInvariant();
    }
}