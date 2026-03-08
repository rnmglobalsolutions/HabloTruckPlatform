using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage.Mappers;

public static class CompanyMapper
{
    public const string Pk = $"{TablePrefixes.Company}";

    public static CompanyEntity ToEntity(Company c)
        => new()
        {
            PartitionKey = Pk,
            RowKey = c.CompanyId,
            Name = c.Name,
            AdminEmailNormalized = c.AdminEmailNormalized,
            StripeCustomerId = c.StripeCustomerId,
            Status = c.Status ?? "active",
            CreatedAtUtc = c.CreatedAtUtc,
            UpdatedAtUtc = c.UpdatedAtUtc
        };

    public static Company FromEntity(CompanyEntity e)
        => new()
        {
            CompanyId = e.RowKey,
            Name = e.Name,
            AdminEmailNormalized = e.AdminEmailNormalized,
            StripeCustomerId = e.StripeCustomerId,
            Status = e.Status,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc
        };
}