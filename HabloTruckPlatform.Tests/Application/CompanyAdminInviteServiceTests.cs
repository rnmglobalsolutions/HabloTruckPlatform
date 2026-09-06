using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace HabloTruckPlatform.Domain.Tests.Application;

public sealed class CompanyAdminInviteServiceTests
{
    [Fact]
    public async Task GetActiveInviteAsync_Should_ReturnNewestValidInvite()
    {
        var companies = new InMemoryCompanyStore();
        var entitlements = new InMemoryEntitlementStore();
        var invites = new InMemoryInviteCodeStore();

        companies.Upsert(new Company { CompanyId = "C_INV_1", StripeCustomerId = "cus_inv_1" });
        invites.Add(new InviteCode
        {
            Code = "HT-OLD111",
            CompanyId = "C_INV_1",
            EntitlementId = "E_INV_1",
            Status = "active",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
            MaxUses = 20,
            Uses = 20
        });
        invites.Add(new InviteCode
        {
            Code = "HT-NEW222",
            CompanyId = "C_INV_1",
            EntitlementId = "E_INV_1",
            Status = "active",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            MaxUses = 20,
            Uses = 4
        });

        var sut = new CompanyAdminInviteService(
            companies,
            entitlements,
            invites,
            NullLogger<CompanyAdminInviteService>.Instance);

        var result = await sut.GetActiveInviteAsync(new CompanyAdminInviteLookupRequest
        {
            StripeCustomerId = "cus_inv_1",
            EntitlementId = "E_INV_1"
        });

        Assert.True(result.Result);
        Assert.True(result.Found);
        Assert.True(result.Valid);
        Assert.Equal("C_INV_1", result.CompanyId);
        Assert.Equal("HT-NEW222", result.Code);
        Assert.Equal(16, result.Remaining);
    }

    [Fact]
    public async Task ResendActiveInviteAsync_Should_CreateInvite_WhenMissing()
    {
        var companies = new InMemoryCompanyStore();
        var entitlements = new InMemoryEntitlementStore();
        var invites = new InMemoryInviteCodeStore();

        companies.Upsert(new Company { CompanyId = "C_INV_2" });
        await entitlements.UpsertAsync(new Entitlement
        {
            CompanyId = "C_INV_2",
            EntitlementId = "E_INV_2",
            SeatsTotal = 20,
            SeatsUsed = 7,
            Status = "active",
            StartUtc = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        var sut = new CompanyAdminInviteService(
            companies,
            entitlements,
            invites,
            NullLogger<CompanyAdminInviteService>.Instance);

        var result = await sut.ResendActiveInviteAsync(new CompanyAdminInviteLookupRequest
        {
            CompanyId = "C_INV_2",
            EntitlementId = "E_INV_2"
        });

        Assert.True(result.Result);
        Assert.True(result.Created);
        Assert.True(result.Resent);
        Assert.True(result.Valid);
        Assert.Equal("C_INV_2", result.CompanyId);
        Assert.Equal("E_INV_2", result.EntitlementId);
        Assert.StartsWith("HT-", result.Code);
        Assert.Equal(20, result.MaxUses);
        Assert.Equal(7, result.Uses);
        Assert.Equal(13, result.Remaining);
    }

    [Fact]
    public async Task EnsureActiveInviteAsync_Should_UpdateExistingInvite_WhenSeatsIncrease()
    {
        var companies = new InMemoryCompanyStore();
        var entitlements = new InMemoryEntitlementStore();
        var invites = new InMemoryInviteCodeStore();

        companies.Upsert(new Company { CompanyId = "C_INV_3" });
        invites.Add(new InviteCode
        {
            Code = "HT-UPD333",
            CompanyId = "C_INV_3",
            EntitlementId = "E_INV_3",
            Status = "active",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            MaxUses = 10,
            Uses = 10,
            CreatedBy = "system:test"
        });

        var sut = new CompanyAdminInviteService(
            companies,
            entitlements,
            invites,
            NullLogger<CompanyAdminInviteService>.Instance);

        var result = await sut.EnsureActiveInviteAsync(
            "C_INV_3",
            "E_INV_3",
            25,
            10,
            "system:test",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.True(result.Result);
        Assert.True(result.Updated);
        Assert.False(result.Created);
        Assert.Equal("HT-UPD333", result.Code);
        Assert.Equal(25, result.MaxUses);
        Assert.Equal(10, result.Uses);
        Assert.Equal(15, result.Remaining);
    }

    private sealed class InMemoryCompanyStore : ICompanyStore
    {
        private readonly Dictionary<string, Company> _companies = new(StringComparer.OrdinalIgnoreCase);

        public void Upsert(Company company)
            => _companies[company.CompanyId] = company;

        public Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
            => Task.FromResult(_companies.TryGetValue(companyId, out var company) ? company : null);

        public Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
            => Task.FromResult(_companies.Values.FirstOrDefault(company =>
                string.Equals(company.StripeCustomerId, stripeCustomerId, StringComparison.OrdinalIgnoreCase)));

        public Task UpsertAsync(Company company, CancellationToken ct = default)
        {
            Upsert(company);
            return Task.CompletedTask;
        }

        public Task UpsertFromCheckoutAsync(string companyId, string? companyName, string? adminEmailNormalized, string? stripeCustomerId, CancellationToken ct = default)
            => throw new NotSupportedException();
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
            => Task.CompletedTask;
    }

    private sealed class InMemoryInviteCodeStore : IInviteCodeStore
    {
        private readonly Dictionary<string, InviteCode> _invites = new(StringComparer.OrdinalIgnoreCase);

        public void Add(InviteCode invite)
            => _invites[invite.Code] = invite;

        public Task EnsureTableAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<InviteCode?> GetAsync(string code, CancellationToken ct = default)
            => Task.FromResult(_invites.TryGetValue(code, out var invite) ? invite : null);

        public Task CreateAsync(InviteCode invite, CancellationToken ct = default)
        {
            if (_invites.ContainsKey(invite.Code))
                throw new InvalidOperationException("Invite exists.");

            _invites[invite.Code] = Clone(invite);
            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeAsync(string code, DateTimeOffset nowUtc, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task UpsertAsync(InviteCode invite, CancellationToken ct = default)
        {
            _invites[invite.Code] = Clone(invite);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InviteCode>> ListForCompanyAsync(string companyId, int take = 100, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InviteCode>>(
                _invites.Values
                    .Where(invite => string.Equals(invite.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(invite => invite.CreatedAtUtc)
                    .Take(take)
                    .Select(Clone)
                    .ToList());

        private static InviteCode Clone(InviteCode invite)
            => new()
            {
                Code = invite.Code,
                CompanyId = invite.CompanyId,
                EntitlementId = invite.EntitlementId,
                Status = invite.Status,
                CreatedAtUtc = invite.CreatedAtUtc,
                ExpiresAtUtc = invite.ExpiresAtUtc,
                MaxUses = invite.MaxUses,
                Uses = invite.Uses,
                CreatedBy = invite.CreatedBy
            };
    }
}
