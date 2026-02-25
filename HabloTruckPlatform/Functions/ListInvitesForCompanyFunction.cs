using System.Net;
using System.Text.Json;
using Azure;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Functions.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class ListInvitesForCompanyFunction
{
    #region Postman use

    // GET https://<func>.azurewebsites.net/api/company/invite/list?companyId=C1&take=50&code=<FUNCTION_KEY>

    // Response:

    /*
     * {
  "ok": true,
  "companyId": "C1",
  "items": [
    {
      "code": "HT-AB12CD",
      "status": "active",
      "createdAtUtc": "2026-02-25T05:00:00.0000000Z",
      "expiresAtUtc": "",
      "maxUses": 25,
      "uses": 3,
      "remaining": 22,
      "valid": true
    }
  ]
}
     */

    #endregion
    private readonly IInviteCodeStore _invites;

    public ListInvitesForCompanyFunction(IInviteCodeStore invites)
    {
        _invites = invites;
    }

    [Function("ListInvitesForCompany")]
    public async Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "company/invite/list")] HttpRequestData req,
    FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;

        string? companyId = null;
        int take = 50;

        if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

            companyId = query["companyId"]?.Trim();

            if (int.TryParse(query["take"], out var t))
                take = Math.Clamp(t, 1, 200);
        }
        else
        {
            try
            {
                var body = await JsonSerializer.DeserializeAsync<ListInvitesForCompanyHttpRequest>(
                    req.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    ct);

                companyId = body?.CompanyId?.Trim();

                if (body?.Take is int t)
                    take = Math.Clamp(t, 1, 200);
            }
            catch
            {
                return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
            }
        }

        if (string.IsNullOrWhiteSpace(companyId))
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "CompanyId required" });

        var invites = await _invites.ListForCompanyAsync(companyId, take, ct);

        var nowUtc = DateTimeOffset.UtcNow;

        var items = invites
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                code = x.Code,
                status = x.Status,
                createdAtUtc = x.CreatedAtUtc.UtcDateTime.ToString("O"),
                expiresAtUtc = x.ExpiresAtUtc?.UtcDateTime.ToString("O") ?? "",
                maxUses = x.MaxUses,
                uses = x.Uses,
                remaining = x.MaxUses <= 0 ? -1 : Math.Max(0, x.MaxUses - x.Uses),
                valid = string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase)
                        && (x.ExpiresAtUtc is null || x.ExpiresAtUtc > nowUtc)
                        && (x.MaxUses <= 0 || x.Uses < x.MaxUses)
            });

        return await Json(req, HttpStatusCode.OK, new { ok = true, companyId, items });
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }
}