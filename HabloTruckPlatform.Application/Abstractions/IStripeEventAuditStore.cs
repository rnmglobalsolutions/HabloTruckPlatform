using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Application.Abstractions;

public interface IStripeEventAuditStore
{
    Task EnsureTableAsync(CancellationToken ct = default);

    Task AppendAsync(
        StripeEventAuditItem item,
        CancellationToken ct = default);

    Task<IReadOnlyList<StripeEventAuditItem>> GetByEventIdAsync(
        string stripeEventId,
        CancellationToken ct = default);

    Task<IReadOnlyList<StripeEventAuditItem>> QueryRecentAsync(
        DateTimeOffset dayUtc,
        int take = 100,
        CancellationToken ct = default);
}