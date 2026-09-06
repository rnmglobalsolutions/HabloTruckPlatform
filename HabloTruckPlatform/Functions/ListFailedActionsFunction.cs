using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class ListFailedActionsFunction
{
    #region Postman / admin usage

    /*
     * Uso:
GET https://<func>/api/admin/failed-actions?status=dead&lookbackHours=48&take=200&code=<FUNCTION_KEY>
     */

    #endregion

    private readonly IFailedActionStore _store;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<ListFailedActionsFunction> _logger;

    public ListFailedActionsFunction(
        IFailedActionStore store,
        IApiKeyValidator apiKeyValidator,
        ILogger<ListFailedActionsFunction> logger)
    {
        _store = store;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("ListFailedActions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "ops/failed-actions")] HttpRequestData req,
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

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

        var status = (query["status"] ?? "dead").Trim().ToLowerInvariant();
        if (status is not ("dead" or "pending" or "succeeded"))
            status = "dead";

        var lookbackHours = 12;
        if (int.TryParse(query["lookbackHours"], out var lh))
            lookbackHours = Math.Clamp(lh, 1, 168);

        var take = 100;
        if (int.TryParse(query["take"], out var t))
            take = Math.Clamp(t, 1, 500);

        var nowUtc = DateTimeOffset.UtcNow;

        var items = await _store.GetByStatusAsync(nowUtc, status, lookbackHours, take, ct);

        var payload = items.Select(x => new
        {
            pk = x.Pk,
            rk = x.Rk,
            actionType = x.ActionType,
            attempts = x.Attempts,
            nextRetryUtc = x.NextRetryUtc.UtcDateTime.ToString("O"),
            // avoid dumping huge payloads; keep first 400 chars
            payload = x.PayloadJson.Length <= 400 ? x.PayloadJson : x.PayloadJson[..400] + "..."
        });

        return await Json(req, HttpStatusCode.OK, new { ok = true, status, lookbackHours, take, items = payload });
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }
}
