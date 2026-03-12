using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class DisableInviteFunction
{
    #region Postman / ManyChat usage
    /*
     * Llamada (Postman / admin)

POST:
https://<func>.azurewebsites.net/api/company/invite/disable?code=<FUNCTION_KEY>

Body:

{ "inviteCode": "HT-AB12CD" }
     */
    #endregion
    private readonly IInviteCodeStore _invites;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<DisableInviteFunction> _logger;

    public DisableInviteFunction(
        IInviteCodeStore invites,
        IApiKeyValidator apiKeyValidator,
        ILogger<DisableInviteFunction> logger)
    {
        _invites = invites;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("DisableInvite")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/invite/disable")] HttpRequestData req,
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

        DisableInviteHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<DisableInviteHttpRequest>(
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
            return await Json(req, HttpStatusCode.NotFound, new { ok = false, error = "Invite not found" });

        invite.Status = "disabled";

        await _invites.UpsertAsync(invite, ct);

        return await Json(req, HttpStatusCode.OK, new
        {
            ok = true,
            code = invite.Code,
            status = invite.Status
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
