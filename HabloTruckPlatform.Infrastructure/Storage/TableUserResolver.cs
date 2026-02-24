using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableUserResolver : IUserResolver
{
    private readonly TableClient _email;
    private readonly TableClient _manyChat;
    private readonly TableClient _stripeCustomer;

    public TableUserResolver(TableServiceClient serviceClient)
    {
        _email = serviceClient.GetTableClient(TableNames.UserEmail);
        _manyChat = serviceClient.GetTableClient(TableNames.UserManyChat);
        _stripeCustomer = serviceClient.GetTableClient(TableNames.UserStripeCustomer);
    }

    public async Task<UserRef?> ResolveByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeCustomerId)) return null;

        var customerId = stripeCustomerId.Trim();

        try
        {
            var pk = Buckets.StripeCustomerLookupPk(customerId);
            var rk = customerId;

            var resp = await _stripeCustomer.GetEntityAsync<UserStripeCustomerLookupEntity>(pk, rk, cancellationToken: ct);
            return new UserRef(resp.Value.UserPk, resp.Value.UserId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<UserRef?> ResolveByManyChatSubscriberIdAsync(string subscriberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberId)) return null;

        var sid = subscriberId.Trim();

        try
        {
            var pk = Buckets.ManyChatLookupPk(sid);
            var rk = sid;

            var resp = await _manyChat.GetEntityAsync<UserManyChatLookupEntity>(pk, rk, cancellationToken: ct);
            return new UserRef(resp.Value.UserPk, resp.Value.UserId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<UserRef?> ResolveByEmailNormalizedAsync(string emailNormalized, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(emailNormalized)) return null;

        // Normalize defensively (even if caller promises it's normalized)
        var email = emailNormalized.Trim().ToLowerInvariant();

        try
        {
            var pk = Buckets.EmailLookupPk(email); // this normalizes internally too; safe
            var rk = email;

            var resp = await _email.GetEntityAsync<UserEmailLookupEntity>(pk, rk, cancellationToken: ct);
            return new UserRef(resp.Value.UserPk, resp.Value.UserId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}