using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Domain.Models;

namespace HabloTruckPlatform.Application.UseCases;

/// <summary>
/// Creates/updates Company + Entitlement after a seat-pack purchase.
/// No Stripe SDK types here.
/// </summary>
public sealed class CheckoutSessionHandler
{
    private readonly IStripeEventStore _stripeEventStore;
    private readonly ICompanyStore _companyStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly IEntitlementExpiryIndexStore _expiryIndexStore;
    private readonly IManyChatSync _manyChatSync;

    public CheckoutSessionHandler(
        IStripeEventStore stripeEventStore,
        ICompanyStore companyStore,
        IEntitlementStore entitlementStore,
        IEntitlementExpiryIndexStore expiryIndexStore,
        IManyChatSync manyChatSync)
    {
        _stripeEventStore = stripeEventStore;
        _companyStore = companyStore;
        _entitlementStore = entitlementStore;
        _expiryIndexStore = expiryIndexStore;
        _manyChatSync = manyChatSync;
    }

    public async Task HandleCheckoutSessionCompletedAsync(
        StripeCheckoutSessionCompleted input,
        CancellationToken ct = default)
    {
        // Idempotency: Stripe retries are normal
        var ok = await _stripeEventStore.TryMarkProcessedAsync(
            input.StripeEventId,
            "checkout.session.completed",
            input.StripeEventCreatedUtc,
            ct);

        if (!ok) return;

        // Validate input (fail-fast)
        if (string.IsNullOrWhiteSpace(input.CompanyId))
            throw new ArgumentException("CompanyId is required.", nameof(input.CompanyId));

        if (input.SeatsTotal <= 0)
            throw new ArgumentException("SeatsTotal must be > 0.", nameof(input.SeatsTotal));

        var term = (input.Term ?? "").Trim().ToLowerInvariant();
        if (term != "annual" && term != "lifetime")
            throw new ArgumentException("Term must be 'annual' or 'lifetime'.", nameof(input.Term));

        // 1) Upsert Company
        await _companyStore.UpsertFromCheckoutAsync(
            companyId: input.CompanyId,
            companyName: input.CompanyName,
            adminEmailNormalized: input.AdminEmailNormalized,
            stripeCustomerId: input.StripeCustomerId,
            ct: ct);

        // 2) Create entitlement record
        var nowUtc = DateTimeOffset.UtcNow; // ok in Application; if you prefer, inject IClock later
        var entitlementId = UlidIds.NewEntitlementId();

        DateTimeOffset? endUtc = term == "annual"
            ? nowUtc.AddYears(1)
            : null; // lifetime

        var entitlement = new Entitlement
        {
            CompanyId = input.CompanyId,
            EntitlementId = entitlementId,
            SeatsTotal = input.SeatsTotal,
            SeatsUsed = 0,
            Status = "active",
            StartUtc = nowUtc,
            EndUtc = endUtc
        };

        await _entitlementStore.CreateAsync(entitlement, ct);

        // 3) If annual, write expiry index row for daily sweeper (cheap queries)
        if (endUtc is not null)
        {
            await _expiryIndexStore.UpsertAsync(
                new EntitlementRef(entitlement.CompanyId, entitlement.EntitlementId),
                endUtc.Value,
                ct);
        }

        // 4) Notify admin (optional)
        try
        {
            await _manyChatSync.NotifyCompanyPackPurchasedAsync(input.CompanyId, input.SeatsTotal, ct);
        }
        catch
        {
            // Best-effort: ignore here; your ManyChat implementation can outbox/retry if desired
        }
    }
}