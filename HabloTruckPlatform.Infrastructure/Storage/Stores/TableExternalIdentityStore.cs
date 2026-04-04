using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableExternalIdentityStore : IExternalIdentityStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableExternalIdentityStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient IdentityTable => _factory.GetClient(TableNames.ExternalIdentities);
    private TableClient LookupTable => _factory.GetClient(TableNames.ExternalIdentityLookup);

    public async Task<ExternalIdentity?> GetAsync(string provider, string externalSubject, CancellationToken ct = default)
    {
        var normalizedProvider = ExternalIdentity.NormalizeProvider(provider);
        var normalizedSubject = ExternalIdentity.NormalizeExternalSubject(externalSubject);
        var rk = BuildRowKey(normalizedProvider, normalizedSubject);
        var pk = Buckets.ExternalIdentityLookupPk(normalizedProvider, normalizedSubject);

        var lookup = await _repo.GetOrNullAsync<ExternalIdentityLookupEntity>(LookupTable, pk, rk, ct);
        if (lookup is null)
            return null;

        var entity = await _repo.GetOrNullAsync<ExternalIdentityEntity>(
            IdentityTable,
            Buckets.ExternalIdentityPk(lookup.UserId),
            rk,
            ct);

        return entity is null ? null : FromEntity(entity);
    }

    public async Task<IReadOnlyList<ExternalIdentity>> ListByUserAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Array.Empty<ExternalIdentity>();

        var results = new List<ExternalIdentity>();
        var pk = Buckets.ExternalIdentityPk(userId.Trim());

        await foreach (var entity in IdentityTable.QueryAsync<ExternalIdentityEntity>(
                           e => e.PartitionKey == pk,
                           cancellationToken: ct))
        {
            results.Add(FromEntity(entity));
        }

        return results;
    }

    public async Task UpsertAsync(ExternalIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var normalizedProvider = ExternalIdentity.NormalizeProvider(identity.Provider);
        var normalizedSubject = ExternalIdentity.NormalizeExternalSubject(identity.ExternalSubject);
        var normalizedChannel = ExternalIdentity.NormalizeChannel(identity.Channel);
        var userId = identity.UserId.Trim();
        var rk = BuildRowKey(normalizedProvider, normalizedSubject);
        var userPk = Buckets.UserBucketPk(userId);
        var lookupPk = Buckets.ExternalIdentityLookupPk(normalizedProvider, normalizedSubject);

        var existingLookup = await _repo.GetOrNullAsync<ExternalIdentityLookupEntity>(LookupTable, lookupPk, rk, ct);
        if (existingLookup is not null
            && !string.Equals(existingLookup.UserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"External identity '{normalizedProvider}|{normalizedSubject}' is already linked to a different user.");
        }

        var existingEntity = await _repo.GetOrNullAsync<ExternalIdentityEntity>(
            IdentityTable,
            Buckets.ExternalIdentityPk(userId),
            rk,
            ct);

        var createdAtUtc = existingEntity?.CreatedAtUtc
            ?? existingLookup?.CreatedAtUtc
            ?? identity.CreatedAtUtc;

        var entity = new ExternalIdentityEntity
        {
            PartitionKey = Buckets.ExternalIdentityPk(userId),
            RowKey = rk,
            UserId = userId,
            Provider = normalizedProvider,
            ExternalSubject = normalizedSubject,
            Channel = normalizedChannel,
            IsPrimary = identity.IsPrimary,
            CreatedAtUtc = createdAtUtc,
            LastSeenAtUtc = identity.LastSeenAtUtc,
            MetadataJson = identity.MetadataJson
        };

        await _repo.UpsertAsync(IdentityTable, entity, TableUpdateMode.Replace, ct);

        var lookup = new ExternalIdentityLookupEntity
        {
            PartitionKey = lookupPk,
            RowKey = rk,
            UserPk = userPk,
            UserId = userId,
            Provider = normalizedProvider,
            ExternalSubject = normalizedSubject,
            Channel = normalizedChannel,
            IsPrimary = identity.IsPrimary,
            CreatedAtUtc = createdAtUtc,
            LastSeenAtUtc = identity.LastSeenAtUtc
        };

        await _repo.UpsertAsync(LookupTable, lookup, TableUpdateMode.Replace, ct);
    }

    private static string BuildRowKey(string provider, string externalSubject)
        => $"{provider}|{externalSubject}";

    private static ExternalIdentity FromEntity(ExternalIdentityEntity entity)
        => new()
        {
            UserId = entity.UserId,
            Provider = entity.Provider,
            ExternalSubject = entity.ExternalSubject,
            Channel = entity.Channel,
            IsPrimary = entity.IsPrimary,
            CreatedAtUtc = entity.CreatedAtUtc,
            LastSeenAtUtc = entity.LastSeenAtUtc,
            MetadataJson = entity.MetadataJson
        };
}
