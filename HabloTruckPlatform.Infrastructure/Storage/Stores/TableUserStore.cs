using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
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
    private readonly IExternalIdentityStore? _externalIdentityStore;

    public TableUserStore(
        ITableClientFactory factory,
        ITableRepository repo,
        IExternalIdentityStore? externalIdentityStore = null)
    {
        _factory = factory;
        _repo = repo;
        _externalIdentityStore = externalIdentityStore;
    }

    private TableClient UsersTable => _factory.GetClient(TableNames.Users);
    private TableClient EmailTable => _factory.GetClient(TableNames.UserEmail);
    private TableClient PhoneTable => _factory.GetClient(TableNames.UserPhone);
    private TableClient ManyChatTable => _factory.GetClient(TableNames.UserManyChat);
    private TableClient StripeCustomerTable => _factory.GetClient(TableNames.UserStripeCustomer);

    public async Task EnsureTablesAsync(CancellationToken ct = default)
    {
        await _factory.EnsureTableAsync(TableNames.Users, ct);
        await _factory.EnsureTableAsync(TableNames.UserEmail, ct);
        await _factory.EnsureTableAsync(TableNames.UserPhone, ct);
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
        => await GetOrCreateByExternalIdentityAsync(
            emailNormalized,
            phoneE164,
            string.IsNullOrWhiteSpace(manyChatSubscriberId) ? null : ExternalIdentityProviders.ManyChat,
            manyChatSubscriberId,
            ExternalIdentityChannels.Unknown,
            ct);

    public async Task<User> GetOrCreateByExternalIdentityAsync(
        string? emailNormalized,
        string? phoneE164,
        string? provider,
        string? externalSubject,
        string? channel,
        CancellationToken ct = default)
    {
        emailNormalized = NormalizeEmail(emailNormalized);
        phoneE164 = NormalizePhone(phoneE164);
        provider = NormalizeProvider(provider);
        externalSubject = NormalizeExternalSubject(externalSubject);
        channel = NormalizeChannel(channel);

        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(externalSubject))
        {
            var existingIdentity = await TryGetByExternalIdentityAsync(provider!, externalSubject!, ct);
            if (existingIdentity is not null)
                return await EnsureResolvedIdentityStateAsync(existingIdentity, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
        }

        // 1) Try resolve existing by email lookup
        if (!string.IsNullOrWhiteSpace(emailNormalized))
        {
            var existing = await TryGetByEmailAsync(emailNormalized!, ct);
            if (existing is not null)
                return await EnsureResolvedIdentityStateAsync(existing, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
        }

        // 2) Try resolve existing by phone lookup
        if (!string.IsNullOrWhiteSpace(phoneE164))
        {
            var existing = await TryGetByPhoneAsync(phoneE164!, ct);
            if (existing is not null)
                return await EnsureResolvedIdentityStateAsync(existing, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
        }

        // 3) Try resolve existing by legacy ManyChat subscriber lookup
        if (string.Equals(provider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(externalSubject))
        {
            var existing = await TryGetByManyChatAsync(externalSubject!, ct);
            if (existing is not null)
                return await EnsureResolvedIdentityStateAsync(existing, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
        }

        // 4) Create new user
        var userId = UlidIds.NewUserId();
        var userPk = Buckets.UserBucketPk(userId);
        var legacyManyChatSubscriberId =
            string.Equals(provider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
                ? externalSubject
                : null;

        var user = new User
        {
            UserId = userId,
            EmailNormalized = emailNormalized,
            ManyChatSubscriberId = legacyManyChatSubscriberId,
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
                if (resolved is not null)
                    return await EnsureResolvedIdentityStateAsync(resolved, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
            }
            if (!string.IsNullOrWhiteSpace(phoneE164))
            {
                var resolved = await TryGetByPhoneAsync(phoneE164!, ct);
                if (resolved is not null)
                    return await EnsureResolvedIdentityStateAsync(resolved, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
            }
            if (!string.IsNullOrWhiteSpace(externalSubject)
                && string.Equals(provider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await TryGetByManyChatAsync(externalSubject!, ct);
                if (resolved is not null)
                    return await EnsureResolvedIdentityStateAsync(resolved, emailNormalized, phoneE164, provider, externalSubject, channel, ct);
            }
            // If still not resolved, return the created one.
        }

        await EnsureExternalIdentityAsync(user, provider, externalSubject, channel, ct);
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
        // PHONE LOOKUP (insert-only)
        // ---------------------------
        if (!string.IsNullOrWhiteSpace(user.PhoneE164))
        {
            var phone = NormalizePhone(user.PhoneE164)!;
            var pk = Buckets.PhoneLookupPk(phone);
            var rk = phone;

            var entity = new UserPhoneLookupEntity
            {
                PartitionKey = pk,
                RowKey = rk,
                UserPk = userPk,
                UserId = user.UserId
            };

            try
            {
                await PhoneTable.AddEntityAsync(entity, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                var existing = await PhoneTable.GetEntityAsync<UserPhoneLookupEntity>(pk, rk, cancellationToken: ct);

                if (existing.Value.UserId != user.UserId)
                {
                    // another user owns this phone -> do nothing
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

        await EnsureLegacyExternalIdentityAsync(user, user.ManyChatSubscriberId, ct);
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

    private async Task<User?> TryGetByPhoneAsync(string phoneE164, CancellationToken ct)
    {
        var phone = NormalizePhone(phoneE164);
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var pk = Buckets.PhoneLookupPk(phone);
        var rk = phone;

        var lookup = await _repo.GetOrNullAsync<UserPhoneLookupEntity>(PhoneTable, pk, rk, ct);
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

    private async Task<User?> TryGetByExternalIdentityAsync(string provider, string externalSubject, CancellationToken ct)
    {
        if (_externalIdentityStore is null)
            return null;

        var identity = await _externalIdentityStore.GetAsync(provider, externalSubject, ct);
        if (identity is null)
            return null;

        return await GetAsync(Buckets.UserBucketPk(identity.UserId), identity.UserId, ct);
    }

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        return email.Trim().ToLowerInvariant();
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        return phone.Trim();
    }

    private static string? NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        return ExternalIdentity.NormalizeProvider(provider);
    }

    private static string? NormalizeExternalSubject(string? externalSubject)
    {
        if (string.IsNullOrWhiteSpace(externalSubject)) return null;
        return ExternalIdentity.NormalizeExternalSubject(externalSubject);
    }

    private static string NormalizeChannel(string? channel)
        => ExternalIdentity.NormalizeChannel(channel);

    private async Task EnsureLegacyExternalIdentityAsync(
        User user,
        string? manyChatSubscriberId,
        CancellationToken ct)
    {
        if (_externalIdentityStore is null || string.IsNullOrWhiteSpace(manyChatSubscriberId))
            return;

        var sid = manyChatSubscriberId.Trim();

        await _externalIdentityStore.UpsertAsync(
            new ExternalIdentity
            {
                UserId = user.UserId,
                Provider = ExternalIdentityProviders.ManyChat,
                ExternalSubject = sid,
                Channel = ExternalIdentityChannels.Unknown,
                IsPrimary = string.Equals(user.ManyChatSubscriberId?.Trim(), sid, StringComparison.OrdinalIgnoreCase),
                CreatedAtUtc = user.UpdatedAtUtc ?? DateTimeOffset.UtcNow,
                LastSeenAtUtc = user.UpdatedAtUtc ?? DateTimeOffset.UtcNow
            },
            ct);
    }

    private async Task EnsureExternalIdentityAsync(
        User user,
        string? provider,
        string? externalSubject,
        string? channel,
        CancellationToken ct)
    {
        if (_externalIdentityStore is null || string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(externalSubject))
        {
            await EnsureLegacyExternalIdentityAsync(user, user.ManyChatSubscriberId, ct);
            return;
        }

        var now = user.UpdatedAtUtc ?? DateTimeOffset.UtcNow;
        var normalizedProvider = NormalizeProvider(provider)!;
        var normalizedSubject = NormalizeExternalSubject(externalSubject)!;
        var normalizedChannel = NormalizeChannel(channel);
        var isManyChatPrimary =
            string.Equals(normalizedProvider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
            && string.Equals(user.ManyChatSubscriberId?.Trim(), normalizedSubject, StringComparison.OrdinalIgnoreCase);

        if (isManyChatPrimary)
        {
            var identities = await _externalIdentityStore.ListByUserAsync(user.UserId, ct);
            foreach (var identity in identities)
            {
                if (!string.Equals(identity.Provider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase))
                    continue;

                var identitySubject = NormalizeExternalSubject(identity.ExternalSubject);
                if (string.IsNullOrWhiteSpace(identitySubject)
                    || string.Equals(identitySubject, normalizedSubject, StringComparison.OrdinalIgnoreCase)
                    || !identity.IsPrimary)
                {
                    continue;
                }

                await _externalIdentityStore.UpsertAsync(
                    new ExternalIdentity
                    {
                        UserId = identity.UserId,
                        Provider = identity.Provider,
                        ExternalSubject = identity.ExternalSubject,
                        Channel = identity.Channel,
                        IsPrimary = false,
                        CreatedAtUtc = identity.CreatedAtUtc,
                        LastSeenAtUtc = identity.LastSeenAtUtc,
                        MetadataJson = identity.MetadataJson
                    },
                    ct);
            }
        }

        await _externalIdentityStore.UpsertAsync(
            new ExternalIdentity
            {
                UserId = user.UserId,
                Provider = normalizedProvider,
                ExternalSubject = normalizedSubject,
                Channel = normalizedChannel,
                IsPrimary = isManyChatPrimary,
                CreatedAtUtc = now,
                LastSeenAtUtc = now
            },
            ct);
    }

    private async Task<User> EnsureResolvedIdentityStateAsync(
        User user,
        string? emailNormalized,
        string? phoneE164,
        string? provider,
        string? externalSubject,
        string? channel,
        CancellationToken ct)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(emailNormalized)
            && !string.Equals(user.EmailNormalized, emailNormalized, StringComparison.OrdinalIgnoreCase))
        {
            user.EmailNormalized = emailNormalized;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(phoneE164)
            && !string.Equals(user.PhoneE164, phoneE164, StringComparison.OrdinalIgnoreCase))
        {
            user.PhoneE164 = phoneE164;
            changed = true;
        }

        if (string.Equals(provider, ExternalIdentityProviders.ManyChat, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(externalSubject)
            && !string.Equals(user.ManyChatSubscriberId, externalSubject, StringComparison.OrdinalIgnoreCase))
        {
            user.ManyChatSubscriberId = externalSubject;
            changed = true;
        }

        if (changed)
        {
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await UpsertAsync(user, ct);
            await UpsertLookupsAsync(user, ct);
        }

        await EnsureExternalIdentityAsync(user, provider, externalSubject, channel, ct);
        return user;
    }

    public async Task<IReadOnlyList<User>> QueryUsersWithStripeAsync(
        int take = 500, CancellationToken ct = default)
    {
        var page = await QueryUsersWithStripePageAsync(take, 0, ct);
        return page.Users;
    }

    public async Task<StripeUserScanPage> QueryUsersWithStripePageAsync(
        int take = 500,
        int startBucket = 0,
        CancellationToken ct = default)
    {
        if (take <= 0)
            return new StripeUserScanPage(Array.Empty<User>(), 0, 0, 0, false);

        var bucketCount = Buckets.UserBucketCount;
        var normalizedStartBucket = ((startBucket % bucketCount) + bucketCount) % bucketCount;
        var results = new List<User>(take);
        var currentBucket = normalizedStartBucket;
        var bucketsScanned = 0;

        while (bucketsScanned < bucketCount)
        {
            var pk = $"{TablePrefixes.User}_{currentBucket:D3}";

            await foreach (var entity in UsersTable.QueryAsync<UserEntity>(
                               e => e.PartitionKey == pk,
                               cancellationToken: ct))
            {
                if (string.IsNullOrWhiteSpace(entity.StripeCustomerId)
                    && string.IsNullOrWhiteSpace(entity.StripeSubscriptionId))
                {
                    continue;
                }

                if (results.Count < take)
                    results.Add(UserMapper.FromEntity(entity));
            }

            currentBucket = (currentBucket + 1) % bucketCount;
            bucketsScanned++;

            if (results.Count >= take)
                break;
        }

        var wrapped = normalizedStartBucket + bucketsScanned >= bucketCount;
        return new StripeUserScanPage(results, normalizedStartBucket, currentBucket, bucketsScanned, wrapped);
    }
}
