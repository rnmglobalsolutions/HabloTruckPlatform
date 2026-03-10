using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Factory;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage.Stores;

public sealed class TableCompanyStore : ICompanyStore
{
    private readonly ITableClientFactory _factory;
    private readonly ITableRepository _repo;

    public TableCompanyStore(ITableClientFactory factory, ITableRepository repo)
    {
        _factory = factory;
        _repo = repo;
    }

    private TableClient CompaniesTable => _factory.GetClient(TableNames.Companies);

    public Task EnsureTableAsync(CancellationToken ct = default)
        => _factory.EnsureTableAsync(TableNames.Companies, ct);

    public async Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId))
            return null;

        var entity = await _repo.GetOrNullAsync<CompanyEntity>(
            CompaniesTable,
            CompanyMapper.Pk,
            companyId.Trim(),
            ct);

        return entity is null ? null : CompanyMapper.FromEntity(entity);
    }

    public async Task<Company?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeCustomerId))
            return null;

        var customerId = stripeCustomerId.Trim();

        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {CompanyMapper.Pk} and StripeCustomerId eq {customerId}");

        await foreach (var entity in CompaniesTable.QueryAsync<CompanyEntity>(
                           filter: filter,
                           maxPerPage: 1,
                           cancellationToken: ct))
        {
            return CompanyMapper.FromEntity(entity);
        }

        return null;
    }

    public async Task UpsertAsync(Company company, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        company.UpdatedAtUtc = now;
        if (company.CreatedAtUtc == default) company.CreatedAtUtc = now;

        var entity = CompanyMapper.ToEntity(company);
        await CompaniesTable.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
    }

    public async Task UpsertFromCheckoutAsync(
        string companyId,
        string? companyName,
        string? adminEmailNormalized,
        string? stripeCustomerId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var existing = await GetAsync(companyId, ct);

        var company = existing ?? new Company
        {
            CompanyId = companyId,
            CreatedAtUtc = now,
            Status = "active"
        };

        company.Name = string.IsNullOrWhiteSpace(companyName) ? company.Name : companyName.Trim();
        company.AdminEmailNormalized = string.IsNullOrWhiteSpace(adminEmailNormalized)
            ? company.AdminEmailNormalized
            : adminEmailNormalized.Trim().ToLowerInvariant();

        company.StripeCustomerId = string.IsNullOrWhiteSpace(stripeCustomerId) ? company.StripeCustomerId : stripeCustomerId.Trim();
        company.UpdatedAtUtc = now;

        await UpsertAsync(company, ct);
    }
}
