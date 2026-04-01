using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class ResendAdminInviteFunction
{
    private readonly CompanyAdminInviteService _inviteService;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<ResendAdminInviteFunction> _logger;

    public ResendAdminInviteFunction(
        CompanyAdminInviteService inviteService,
        IApiKeyValidator apiKeyValidator,
        ILogger<ResendAdminInviteFunction> logger)
    {
        _inviteService = inviteService;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("ResendAdminInvite")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/invite/resend")] HttpRequestData req,
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

        ResendAdminInviteHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ResendAdminInviteHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var result = await _inviteService.ResendActiveInviteAsync(new CompanyAdminInviteLookupRequest
        {
            CompanyId = body?.CompanyId,
            StripeCustomerId = body?.StripeCustomerId,
            EntitlementId = body?.EntitlementId
        }, ct);

        var status = result.Result
            ? HttpStatusCode.OK
            : result.Error is "entitlement_not_found" or "company_not_found" or "active_invite_not_found"
                ? HttpStatusCode.NotFound
                : HttpStatusCode.Conflict;

        return await Json(req, status, new
        {
            ok = result.Result,
            found = result.Found,
            created = result.Created,
            resent = result.Resent,
            updated = result.Updated,
            valid = result.Valid,
            error = result.Error,
            companyId = result.CompanyId,
            entitlementId = result.EntitlementId,
            code = result.Code,
            status = result.Status,
            createdBy = result.CreatedBy,
            maxUses = result.MaxUses,
            uses = result.Uses,
            remaining = result.Remaining,
            createdAtUtc = result.CreatedAtUtc == default ? "" : result.CreatedAtUtc.UtcDateTime.ToString("O"),
            expiresAtUtc = result.ExpiresAtUtc?.UtcDateTime.ToString("O") ?? ""
        });
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }
}
