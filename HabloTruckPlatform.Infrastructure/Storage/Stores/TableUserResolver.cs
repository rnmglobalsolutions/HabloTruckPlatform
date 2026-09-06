using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableUserResolver : IUserResolver
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableUserResolver(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient EmailTable => _factory.GetClient(TableNames.UserEmail);
    private TableClient PhoneTable => _factory.GetClient(TableNames.UserPhone);
    private TableClient ManyChatTable => _factory.GetClient(TableNames.UserManyChat);
    private TableClient StripeCustomerTable => _factory.GetClient(TableNames.UserStripeCustomer);
    private TableClient ExternalIdentityLookupTable => _factory.GetClient(TableNames.ExternalIdentityLookup);

    public async Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeCustomerId)) return null;

        var customerId = stripeCustomerId.Trim();

        var pk = Buckets.StripeCustomerLookupPk(customerId);
        var rk = customerId;

        var entity = await _repo.GetOrNullAsync<UserStripeCustomerLookupEntity>(StripeCustomerTable, pk, rk, ct);
        return entity is null ? null : new UserRef(entity.UserPk, entity.UserId);
    }

    public async Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberId)) return null;

        var sid = subscriberId.Trim();

        var pk = Buckets.ManyChatLookupPk(sid);
        var rk = sid;

        var entity = await _repo.GetOrNullAsync<UserManyChatLookupEntity>(ManyChatTable, pk, rk, ct);
        return entity is null ? null : new UserRef(entity.UserPk, entity.UserId);
    }

    public async Task<UserRef?> ResolveByExternalIdentityAsync(string provider, string externalSubject, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(externalSubject))
            return null;

        var normalizedProvider = HabloTruckPlatform.Domain.Models.ExternalIdentity.NormalizeProvider(provider);
        var normalizedSubject = HabloTruckPlatform.Domain.Models.ExternalIdentity.NormalizeExternalSubject(externalSubject);
        var rk = $"{normalizedProvider}|{normalizedSubject}";
        var pk = Buckets.ExternalIdentityLookupPk(normalizedProvider, normalizedSubject);

        var entity = await _repo.GetOrNullAsync<ExternalIdentityLookupEntity>(ExternalIdentityLookupTable, pk, rk, ct);
        return entity is null ? null : new UserRef(entity.UserPk, entity.UserId);
    }

    public async Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized)) return null;

        // Normalize defensively (even if caller promises it's normalized)
        var email = emailNormalized.Trim().ToLowerInvariant();

        var pk = Buckets.EmailLookupPk(email);
        var rk = email;

        var entity = await _repo.GetOrNullAsync<UserEmailLookupEntity>(EmailTable, pk, rk, ct);
        return entity is null ? null : new UserRef(entity.UserPk, entity.UserId);
    }

    public async Task<UserRef?> ResolveByPhoneE164Async(string phoneE164, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneE164)) return null;

        var phone = phoneE164.Trim();
        var pk = Buckets.PhoneLookupPk(phone);
        var rk = phone;

        var entity = await _repo.GetOrNullAsync<UserPhoneLookupEntity>(PhoneTable, pk, rk, ct);
        return entity is null ? null : new UserRef(entity.UserPk, entity.UserId);
    }
}
