using Azure;
using Azure.Data.Tables;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Infrastructure.Storage.Entities;

namespace HabloTruckPlatform.Infrastructure.Storage;

public sealed class TableInviteCodeStore : IInviteCodeStore
{
    private readonly TableClient _table;
    private readonly TableClient _companyIdx;
    private static string CompanyPk(string companyId) => $"HT#INVC#{companyId}";

    public TableInviteCodeStore(TableServiceClient svc)
    {
        _table = svc.GetTableClient(TableNames.InviteCodes);
        _companyIdx = svc.GetTableClient(TableNames.InviteCompanyIndex);
    }

    public async Task EnsureTableAsync(CancellationToken ct = default)
    {
        await _table.CreateIfNotExistsAsync(ct);
        await _companyIdx.CreateIfNotExistsAsync(ct);
    }

    public async Task<InviteCode?> GetAsync(string code, CancellationToken ct = default)
    {
        var c = Normalize(code);
        if (string.IsNullOrWhiteSpace(c)) return null;

        try
        {
            var pk = Pk(c);
            var resp = await _table.GetEntityAsync<InviteCodeEntity>(pk, c, cancellationToken: ct);
            return FromEntity(resp.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task CreateAsync(InviteCode invite, CancellationToken ct = default)
    {
        invite.Code = Normalize(invite.Code) ?? throw new ArgumentException("Code required");
        invite.CreatedAtUtc = invite.CreatedAtUtc == default ? DateTimeOffset.UtcNow : invite.CreatedAtUtc;

        var e = ToEntity(invite);
        e.PartitionKey = Pk(invite.Code);
        e.RowKey = invite.Code;

        await _table.AddEntityAsync(e, ct);

        // write company index (best-effort)
        try
        {
            var idx = new InviteCompanyIndexEntity
            {
                PartitionKey = CompanyPk(invite.CompanyId),
                RowKey = $"{invite.CreatedAtUtc.Ticks:D19}_{invite.Code}",
                CompanyId = invite.CompanyId,
                Code = invite.Code,
                CreatedAtUtc = invite.CreatedAtUtc,
                Status = invite.Status ?? "active"
            };

            await _companyIdx.UpsertEntityAsync(idx, TableUpdateMode.Replace, ct);
        }
        catch
        {
            // ignore index failures; listing may miss it but invite still exists
        }
    }

    public async Task<bool> TryConsumeAsync(string code, DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var c = Normalize(code);
        if (string.IsNullOrWhiteSpace(c)) return false;

        // retry once on ETag conflict
        for (var attempt = 0; attempt < 2; attempt++)
        {
            InviteCodeEntity entity;

            try
            {
                var resp = await _table.GetEntityAsync<InviteCodeEntity>(Pk(c), c, cancellationToken: ct);
                entity = resp.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }

            if (!string.Equals(entity.Status, "active", StringComparison.OrdinalIgnoreCase))
                return false;

            if (entity.ExpiresAtUtc is not null && entity.ExpiresAtUtc <= nowUtc)
                return false;

            if (entity.MaxUses > 0 && entity.Uses >= entity.MaxUses)
                return false;

            entity.Uses += 1;

            try
            {
                await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
            {
                // concurrency conflict -> retry
            }
        }

        return false;
    }

    public async Task UpsertAsync(InviteCode invite, CancellationToken ct = default)
    {
        invite.Code = Normalize(invite.Code) ?? throw new ArgumentException("Code required");

        var e = ToEntity(invite);
        e.PartitionKey = Pk(invite.Code);
        e.RowKey = invite.Code;

        await _table.UpsertEntityAsync(e, TableUpdateMode.Replace, ct);

        try
        {
            var idxFilter = TableClient.CreateQueryFilter(
                $"PartitionKey eq {CompanyPk(invite.CompanyId)} and Code eq {invite.Code}");

            await foreach (var row in _companyIdx.QueryAsync<InviteCompanyIndexEntity>(filter: idxFilter, maxPerPage: 10, cancellationToken: ct))
            {
                row.Status = invite.Status ?? row.Status;
                await _companyIdx.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, ct);
                break;
            }
        }
        catch
        {
            // ignore
        }
    }

    public async Task<IReadOnlyList<InviteCode>> ListForCompanyAsync(string companyId, int take = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyId)) return Array.Empty<InviteCode>();
        if (take <= 0) take = 100;

        var pk = CompanyPk(companyId.Trim());

        // newest first by RowKey? RowKey is ticks+code ascending.
        // We'll just take last N by collecting and ordering desc in memory.
        var idxRows = new List<InviteCompanyIndexEntity>(Math.Min(take, 200));

        await foreach (var row in _companyIdx.QueryAsync<InviteCompanyIndexEntity>(
            filter: TableClient.CreateQueryFilter($"PartitionKey eq {pk}"),
            maxPerPage: 200,
            cancellationToken: ct))
        {
            idxRows.Add(row);
            if (idxRows.Count >= 500) break;
        }

        var codes = idxRows
            .OrderByDescending(x => x.RowKey)
            .Take(take)
            .Select(x => x.Code)
            .Distinct()
            .ToList();

        var results = new List<InviteCode>(codes.Count);

        foreach (var code in codes)
        {
            var inv = await GetAsync(code, ct);
            if (inv is not null) results.Add(inv);
        }

        return results;
    }

    // ----------------- mapping/helpers -----------------

    private static InviteCodeEntity ToEntity(InviteCode i) => new()
    {
        PartitionKey = "", // filled by caller
        RowKey = "",       // filled by caller
        Code = i.Code,
        CompanyId = i.CompanyId,
        EntitlementId = i.EntitlementId,
        Status = i.Status ?? "active",
        CreatedAtUtc = i.CreatedAtUtc,
        ExpiresAtUtc = i.ExpiresAtUtc,
        MaxUses = i.MaxUses,
        Uses = i.Uses,
        CreatedBy = i.CreatedBy
    };

    private static InviteCode FromEntity(InviteCodeEntity e) => new()
    {
        Code = e.Code,
        CompanyId = e.CompanyId,
        EntitlementId = e.EntitlementId,
        Status = e.Status,
        CreatedAtUtc = e.CreatedAtUtc,
        ExpiresAtUtc = e.ExpiresAtUtc,
        MaxUses = e.MaxUses,
        Uses = e.Uses,
        CreatedBy = e.CreatedBy
    };

    private static string Pk(string codeNormalized)
    {
        var prefix = codeNormalized.Length >= 2 ? codeNormalized[..2] : "xx";
        return $"HT#INV#{prefix}";
    }

    private static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code.Trim().ToUpperInvariant();
    }
}