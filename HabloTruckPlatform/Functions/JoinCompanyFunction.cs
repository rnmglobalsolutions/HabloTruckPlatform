using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Domain.Ids;
using HabloTruckPlatform.Functions.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class JoinCompanyFunction
{
    private readonly IUserStore _users;
    private readonly IInviteCodeStore _invites;
    private readonly CompanyJoinHandler _join;

    #region En ManyChat, cuando el user ponga el invite code:
    /*
     * En ManyChat, cuando el user ponga el invite code:

Action → External Request

POST a:
https://<tu-func>.azurewebsites.net/api/company/join?code=<FUNCTION_KEY>

Body JSON:

{
  "inviteCode": "{{cf_invite_code}}",
  "manyChatSubscriberId": "{{subscriber.id}}",
  "email": "{{subscriber.email}}"
}
     */
    #endregion

    public JoinCompanyFunction(IUserStore users, IInviteCodeStore invites, CompanyJoinHandler join)
    {
        _users = users;
        _invites = invites;
        _join = join;
    }

    [Function("JoinCompany")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/join")] HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;

        JoinCompanyHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<JoinCompanyHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var inviteCode = body?.InviteCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(inviteCode))
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "InviteCode required" });

        if (string.IsNullOrWhiteSpace(body?.ManyChatSubscriberId) && string.IsNullOrWhiteSpace(body?.Email))
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "ManyChatSubscriberId or Email required" });

        // 1) Load invite
        var invite = await _invites.GetAsync(inviteCode, ct);
        if (invite is null)
            return await Json(req, HttpStatusCode.NotFound, new { ok = false, error = "Invite not found" });

        // 2) Resolve/create user (subscriberId preferred)
        var user = await _users.GetOrCreateAsync(
            emailNormalized: body?.Email,
            manyChatSubscriberId: body?.ManyChatSubscriberId,
            phoneE164: body?.PhoneE164,
            ct: ct);

        // 3) Idempotency: already in this company -> do NOT consume invite
        if (!string.IsNullOrWhiteSpace(user.CompanyId) &&
            string.Equals(user.CompanyId, invite.CompanyId, StringComparison.OrdinalIgnoreCase))
        {
            return await Json(req, HttpStatusCode.OK, new
            {
                ok = true,
                alreadyJoined = true,
                companyId = invite.CompanyId,
                entitlementId = invite.EntitlementId
            });
        }

        // 4) If user is already in a different company, block (recommended)
        if (!string.IsNullOrWhiteSpace(user.CompanyId) &&
            !string.Equals(user.CompanyId, invite.CompanyId, StringComparison.OrdinalIgnoreCase))
        {
            return await Json(req, HttpStatusCode.Conflict, new
            {
                ok = false,
                error = "UserAlreadyInAnotherCompany",
                currentCompanyId = user.CompanyId,
                requestedCompanyId = invite.CompanyId
            });
        }

        // 5) Consume invite use (only now)
        var consumed = await _invites.TryConsumeAsync(inviteCode, DateTimeOffset.UtcNow, ct);
        if (!consumed)
            return await Json(req, HttpStatusCode.Conflict, new { ok = false, error = "Invite expired/disabled/exhausted" });

        // 6) Join company
        var userPk = Buckets.UserBucketPk(user.UserId);

        await _join.HandleJoinAsync(
            new JoinCompanyRequest(
                UserPk: userPk,
                UserId: user.UserId,
                CompanyId: invite.CompanyId,
                EntitlementId: invite.EntitlementId,
                InviteCode: invite.Code),
            ct);

        return await Json(req, HttpStatusCode.OK, new
        {
            ok = true,
            alreadyJoined = false,
            companyId = invite.CompanyId,
            entitlementId = invite.EntitlementId
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