using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Domain.Models;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Functions.Infrastructure;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class CreateInviteFunction
{
    private readonly IInviteCodeStore _invites;
    private readonly IEntitlementStore _entitlements;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<CreateInviteFunction> _logger;

    #region ManyChat / Postman usage
    /*
     * ManyChat / Postman usage
        Create invite

        POST:
        https://<func>.azurewebsites.net/api/company/invite?code=<FUNCTION_KEY>

        Body:

        {
            "companyId": "C1",
            "entitlementId": "E1",
            "maxUses": 25,
            "expiresInDays": 30,
            "createdBy": "admin@decano.ai"
        }

        Response:

        {
            "ok": true,
            "code": "HT-AB12CD",
            "companyId": "C1",
            "entitlementId": "E1",
            "maxUses": 25,
            "expiresAtUtc": "2026-02-24T05:00:00.0000000Z"
        }
     */
    #endregion

    public CreateInviteFunction(
        IInviteCodeStore invites,
        IEntitlementStore entitlements,
        IApiKeyValidator apiKeyValidator,
        ILogger<CreateInviteFunction> logger)
    {
        _invites = invites;
        _entitlements = entitlements;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("CreateInvite")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/invite")] HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;

        var unauthorized = await ApiKeyAuthorizationHelper.AuthorizeAsync(
            req,
            _apiKeyValidator,
            _logger,
            ct);

        if (unauthorized is not null)
            return unauthorized;

        CreateInviteHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CreateInviteHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var companyId = body?.CompanyId?.Trim();
        var entitlementId = body?.EntitlementId?.Trim();

        if (string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(entitlementId))
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "CompanyId and EntitlementId are required" });

        // Validate entitlement exists + active (soft)
        var ent = await _entitlements.GetAsync(companyId, entitlementId, ct);
        if (ent is null)
            return await Json(req, HttpStatusCode.NotFound, new { ok = false, error = "Entitlement not found" });

        if (!string.Equals(ent.Status, "active", StringComparison.OrdinalIgnoreCase))
            return await Json(req, HttpStatusCode.Conflict, new { ok = false, error = $"Entitlement status is '{ent.Status}'" });

        var maxUses = body?.MaxUses ?? 25;
        if (maxUses < 1) maxUses = 1;

        DateTimeOffset? expiresAt = null;
        if (body?.ExpiresInDays is not null)
        {
            var days = body.ExpiresInDays.Value;
            if (days < 1) days = 1;
            expiresAt = DateTimeOffset.UtcNow.AddDays(days);
        }

        // Code: use provided or generate
        var code = string.IsNullOrWhiteSpace(body?.Code)
            ? InviteCodeGenerator.NewCode("HT", len: 6)
            : InviteCodeGenerator.Normalize(body!.Code!);

        // Create (retry on collision)
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var invite = new InviteCode
            {
                Code = code,
                CompanyId = companyId,
                EntitlementId = entitlementId,
                Status = "active",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = expiresAt,
                MaxUses = maxUses,
                Uses = 0,
                CreatedBy = string.IsNullOrWhiteSpace(body?.CreatedBy) ? null : body!.CreatedBy!.Trim()
            };

            try
            {
                await _invites.CreateAsync(invite, ct);

                return await Json(req, HttpStatusCode.OK, new
                {
                    ok = true,
                    code = invite.Code,
                    companyId = invite.CompanyId,
                    entitlementId = invite.EntitlementId,
                    maxUses = invite.MaxUses,
                    expiresAtUtc = invite.ExpiresAtUtc?.UtcDateTime.ToString("O") ?? ""
                });
            }
            catch
            {
                // likely collision, generate another
                code = InviteCodeGenerator.NewCode("HT", len: 6);
            }
        }

        return await Json(req, HttpStatusCode.InternalServerError, new { ok = false, error = "Could not create invite (collisions)" });
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }
}
