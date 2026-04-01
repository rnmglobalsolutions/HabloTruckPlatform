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

public sealed class GetActiveInviteFunction
{
    private readonly CompanyAdminInviteService _inviteService;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<GetActiveInviteFunction> _logger;

    public GetActiveInviteFunction(
        CompanyAdminInviteService inviteService,
        IApiKeyValidator apiKeyValidator,
        ILogger<GetActiveInviteFunction> logger)
    {
        _inviteService = inviteService;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("GetActiveInvite")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "company/invite/active")] HttpRequestData req,
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

        CompanyAdminInviteLookupRequest request;

        if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            request = new CompanyAdminInviteLookupRequest
            {
                CompanyId = query["companyId"],
                StripeCustomerId = query["stripeCustomerId"],
                EntitlementId = query["entitlementId"]
            };
        }
        else
        {
            GetActiveInviteHttpRequest? body;
            try
            {
                body = await JsonSerializer.DeserializeAsync<GetActiveInviteHttpRequest>(
                    req.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    ct);
            }
            catch
            {
                return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
            }

            request = new CompanyAdminInviteLookupRequest
            {
                CompanyId = body?.CompanyId,
                StripeCustomerId = body?.StripeCustomerId,
                EntitlementId = body?.EntitlementId
            };
        }

        var result = await _inviteService.GetActiveInviteAsync(request, ct);
        var status = result.Result ? HttpStatusCode.OK : HttpStatusCode.NotFound;

        return await Json(req, status, new
        {
            ok = result.Result,
            found = result.Found,
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
