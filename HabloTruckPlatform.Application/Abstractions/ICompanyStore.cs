using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface ICompanyStore
{
    Task<Company?> GetAsync(string companyId, CancellationToken ct = default);
    Task UpsertAsync(Company company, CancellationToken ct = default);

    /// <summary>
    /// For checkout.session.completed (seat pack purchase): upsert Company using metadata and Stripe customer id.
    /// </summary>
    Task UpsertFromCheckoutAsync(
        string companyId, 
        string? companyName, 
        string? adminEmailNormalized, 
        string? stripeCustomerId, 
        CancellationToken ct = default);
}