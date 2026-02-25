using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Functions.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

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

if valid == true → show “✅ Código válido, continuamos”

else → show “❌ Código inválido / expirado / cupo lleno”
     */
    #endregion

    private readonly IInviteCodeStore _invites;

    public GetInviteInfoFunction(IInviteCodeStore invites)
    {
        _invites = invites;
    }

    [Function("GetInviteInfo")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/invite/info")] HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;

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