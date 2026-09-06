using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class GetInviteInfoFunction
{
    #region Manychat / Postman usage
    /*
     * ManyChat usage (validation step)

External Request:
POST:
https://<func>.azurewebsites.net/api/company/invite/info?code=<FUNCTION_KEY>

Body:

{
  "inviteCode": "{{cf_invite_code}}"
}

Then in ManyChat:

if valid == true â†’ show â€œâœ… CÃ³digo vÃ¡lido, continuamosâ€

else â†’ show â€œâŒ CÃ³digo invÃ¡lido / expirado / cupo llenoâ€
     */
    #endregion

    private readonly IInviteCodeStore _invites;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<GetInviteInfoFunction> _logger;

    public GetInviteInfoFunction(
        IInviteCodeStore invites,
        IApiKeyValidator apiKeyValidator,
        ILogger<GetInviteInfoFunction> logger)
    {
        _invites = invites;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("GetInviteInfo")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/invite/info")] HttpRequestData req,
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

        GetInviteInfoHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<GetInviteInfoHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var code = body?.InviteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "InviteCode required" });

        var invite = await _invites.GetAsync(code, ct);
        if (invite is null)
            return await Json(req, HttpStatusCode.NotFound, new { ok = false, valid = false, error = "Invite not found" });

        var nowUtc = DateTimeOffset.UtcNow;

        var active = string.Equals(invite.Status, "active", StringComparison.OrdinalIgnoreCase);
        var notExpired = invite.ExpiresAtUtc is null || invite.ExpiresAtUtc > nowUtc;
        var remaining = invite.MaxUses <= 0 ? int.MaxValue : Math.Max(0, invite.MaxUses - invite.Uses);
        var hasCapacity = invite.MaxUses <= 0 || invite.Uses < invite.MaxUses;

        var valid = active && notExpired && hasCapacity;

        return await Json(req, HttpStatusCode.OK, new
        {
            ok = true,
            valid,
            code = invite.Code,
            status = invite.Status,
            companyId = invite.CompanyId,
            entitlementId = invite.EntitlementId,
            expiresAtUtc = invite.ExpiresAtUtc?.UtcDateTime.ToString("O") ?? "",
            maxUses = invite.MaxUses,
            uses = invite.Uses,
            remaining = remaining == int.MaxValue ? -1 : remaining
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
