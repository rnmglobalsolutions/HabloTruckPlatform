using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Domain.Models;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace HabloTruckPlatform.Application.UseCases;

public sealed class CompanyAdminInviteService
{
    private readonly ICompanyStore _companyStore;
    private readonly IEntitlementStore _entitlementStore;
    private readonly IInviteCodeStore _inviteCodeStore;
    private readonly ILogger<CompanyAdminInviteService> _logger;

    public CompanyAdminInviteService(
        ICompanyStore companyStore,
        IEntitlementStore entitlementStore,
        IInviteCodeStore inviteCodeStore,
        ILogger<CompanyAdminInviteService> logger)
    {
        _companyStore = companyStore;
        _entitlementStore = entitlementStore;
        _inviteCodeStore = inviteCodeStore;
        _logger = logger;
    }

    public async Task<CompanyAdminInviteResult> GetActiveInviteAsync(
        CompanyAdminInviteLookupRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail("invalid_request");

        var companyId = await ResolveCompanyIdAsync(request, ct);
        if (string.IsNullOrWhiteSpace(companyId))
            return Fail("company_not_found");

        var invite = await FindActiveInviteAsync(companyId, request.EntitlementId, DateTimeOffset.UtcNow, ct);
        if (invite is null)
            return Fail("active_invite_not_found", companyId, request.EntitlementId);

        return ToResult(invite, found: true, valid: true);
    }

    public async Task<CompanyAdminInviteResult> ResendActiveInviteAsync(
        CompanyAdminInviteLookupRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            return Fail("invalid_request");

        var companyId = await ResolveCompanyIdAsync(request, ct);
        if (string.IsNullOrWhiteSpace(companyId))
            return Fail("company_not_found");

        var nowUtc = DateTimeOffset.UtcNow;
        var invite = await FindActiveInviteAsync(companyId, request.EntitlementId, nowUtc, ct);
        if (invite is not null)
        {
            var existing = ToResult(invite, found: true, valid: true);
            existing.Resent = true;
            return existing;
        }

        if (string.IsNullOrWhiteSpace(request.EntitlementId))
            return Fail("active_invite_not_found", companyId, null);

        var entitlement = await _entitlementStore.GetAsync(companyId, request.EntitlementId.Trim(), ct);
        if (entitlement is null)
            return Fail("entitlement_not_found", companyId, request.EntitlementId);

        if (!entitlement.IsActive(nowUtc))
            return Fail("entitlement_not_active", companyId, entitlement.EntitlementId);

        var ensured = await EnsureActiveInviteAsync(
            companyId,
            entitlement.EntitlementId,
            entitlement.SeatsTotal,
            entitlement.SeatsUsed,
            "system:admin_invite_resend",
            nowUtc,
            ct);

        ensured.Resent = ensured.Result;
        return ensured;
    }

    public async Task<CompanyAdminInviteResult> EnsureActiveInviteAsync(
        string companyId,
        string entitlementId,
        int maxUses,
        int usesFloor,
        string createdBy,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        companyId = companyId?.Trim() ?? "";
        entitlementId = entitlementId?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return Fail("company_and_entitlement_required", companyId, entitlementId);

        var normalizedMaxUses = NormalizeMaxUses(maxUses, usesFloor);
        var normalizedUsesFloor = Math.Max(0, Math.Min(usesFloor, normalizedMaxUses));

        var invites = await _inviteCodeStore.ListForCompanyAsync(companyId, take: 500, ct);
        var existing = invites
            .Where(invite =>
                string.Equals(invite.EntitlementId, entitlementId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(invite.Status, "active", StringComparison.OrdinalIgnoreCase)
                && (invite.ExpiresAtUtc is null || invite.ExpiresAtUtc > nowUtc))
            .OrderByDescending(invite => invite.CreatedAtUtc)
            .FirstOrDefault();

        if (existing is not null)
        {
            var desiredMaxUses = Math.Max(normalizedMaxUses, existing.Uses);
            var desiredUses = Math.Max(existing.Uses, normalizedUsesFloor);
            var needsUpdate = existing.MaxUses != desiredMaxUses || existing.Uses != desiredUses;

            if (needsUpdate)
            {
                existing.MaxUses = desiredMaxUses;
                existing.Uses = desiredUses;
                await _inviteCodeStore.UpsertAsync(existing, ct);

                _logger.LogInformation(
                    "Operation step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} CompanyId={CompanyId} EntitlementId={EntitlementId} InviteCode={InviteCode} MaxUses={MaxUses} Uses={Uses}",
                    "step",
                    "company_admin_invite_ensure",
                    "updated",
                    companyId,
                    entitlementId,
                    existing.Code,
                    existing.MaxUses,
                    existing.Uses);
            }

            var result = ToResult(existing, found: true, valid: IsValid(existing, nowUtc));
            result.Updated = needsUpdate;
            return result;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var invite = new InviteCode
            {
                Code = GenerateInviteCode(),
                CompanyId = companyId,
                EntitlementId = entitlementId,
                Status = "active",
                CreatedAtUtc = nowUtc,
                MaxUses = normalizedMaxUses,
                Uses = normalizedUsesFloor,
                CreatedBy = createdBy
            };

            try
            {
                await _inviteCodeStore.CreateAsync(invite, ct);

                _logger.LogInformation(
                    "Operation step completed. LogCategory={LogCategory} Step={Step} Outcome={Outcome} CompanyId={CompanyId} EntitlementId={EntitlementId} InviteCode={InviteCode} MaxUses={MaxUses} Uses={Uses}",
                    "step",
                    "company_admin_invite_ensure",
                    "created",
                    companyId,
                    entitlementId,
                    invite.Code,
                    invite.MaxUses,
                    invite.Uses);

                var created = ToResult(invite, found: true, valid: true);
                created.Created = true;
                return created;
            }
            catch (InvalidOperationException)
            {
                // Invite code collision; retry with a fresh code.
            }
        }

        return Fail("invite_code_collision_retries_exhausted", companyId, entitlementId);
    }

    private async Task<string?> ResolveCompanyIdAsync(CompanyAdminInviteLookupRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.CompanyId))
            return request.CompanyId.Trim();

        if (string.IsNullOrWhiteSpace(request.StripeCustomerId))
            return null;

        var company = await _companyStore.GetByStripeCustomerIdAsync(request.StripeCustomerId.Trim(), ct);
        return company?.CompanyId;
    }

    private async Task<InviteCode?> FindActiveInviteAsync(
        string companyId,
        string? entitlementId,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var invites = await _inviteCodeStore.ListForCompanyAsync(companyId, take: 500, ct);
        return invites
            .Where(invite =>
                MatchesEntitlement(invite, entitlementId)
                && IsValid(invite, nowUtc))
            .OrderByDescending(invite => invite.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static bool MatchesEntitlement(InviteCode invite, string? entitlementId)
        => string.IsNullOrWhiteSpace(entitlementId)
            || string.Equals(invite.EntitlementId, entitlementId.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsValid(InviteCode invite, DateTimeOffset nowUtc)
    {
        if (!string.Equals(invite.Status, "active", StringComparison.OrdinalIgnoreCase))
            return false;

        if (invite.ExpiresAtUtc is not null && invite.ExpiresAtUtc <= nowUtc)
            return false;

        return invite.MaxUses <= 0 || invite.Uses < invite.MaxUses;
    }

    private static int NormalizeMaxUses(int maxUses, int usesFloor)
        => Math.Max(1, Math.Max(maxUses, usesFloor));

    private static CompanyAdminInviteResult ToResult(InviteCode invite, bool found, bool valid)
    {
        var remaining = invite.MaxUses <= 0 ? -1 : Math.Max(0, invite.MaxUses - invite.Uses);

        return new CompanyAdminInviteResult
        {
            Result = true,
            Found = found,
            Valid = valid,
            CompanyId = invite.CompanyId,
            EntitlementId = invite.EntitlementId,
            Code = invite.Code,
            Status = invite.Status,
            CreatedBy = invite.CreatedBy,
            MaxUses = invite.MaxUses,
            Uses = invite.Uses,
            Remaining = remaining,
            CreatedAtUtc = invite.CreatedAtUtc,
            ExpiresAtUtc = invite.ExpiresAtUtc
        };
    }

    private static CompanyAdminInviteResult Fail(string error, string? companyId = null, string? entitlementId = null)
        => new()
        {
            Result = false,
            Error = error,
            CompanyId = companyId,
            EntitlementId = entitlementId
        };

    private static string GenerateInviteCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        const int len = 6;

        Span<byte> bytes = stackalloc byte[len];
        RandomNumberGenerator.Fill(bytes);

        Span<char> chars = stackalloc char[len];
        for (var i = 0; i < len; i++)
            chars[i] = alphabet[bytes[i] % alphabet.Length];

        return $"HT-{new string(chars)}";
    }
}
