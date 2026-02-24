using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;
using HabloTruckPlatform.Infrastructure.Storage.Mappers;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableCompanyStore : ICompanyStore
{
    private readonly TableClient _companies;

    public TableCompanyStore(TableServiceClient serviceClient)
    {
        _companies = serviceClient.GetTableClient(TableNames.Companies);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
        => await _companies.CreateIfNotExistsAsync(ct);

    public async Task<Company?> GetAsync(string companyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId)) return null;

        try
        {
            var resp = await _companies.GetEntityAsync<CompanyEntity>(
                CompanyMapper.Pk, companyId.Trim(), cancellationToken: ct);

            return CompanyMapper.FromEntity(resp.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertAsync(Company company, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        company.UpdatedAtUtc = now;
        if (company.CreatedAtUtc == default) company.CreatedAtUtc = now;

        var entity = CompanyMapper.ToEntity(company);
        await _companies.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
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