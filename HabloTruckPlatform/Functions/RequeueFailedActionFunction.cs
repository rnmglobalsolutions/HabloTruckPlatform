using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Abstractions;
using HabloTruckPlatform.Functions.Contracts;
using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class RequeueFailedActionFunction
{
    private readonly IFailedActionStore _store;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<RequeueFailedActionFunction> _logger;

    public RequeueFailedActionFunction(
        IFailedActionStore store,
        IApiKeyValidator apiKeyValidator,
        ILogger<RequeueFailedActionFunction> logger)
    {
        _store = store;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("RequeueFailedAction")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "admin/failed-actions/requeue")] HttpRequestData req,
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

        RequeueFailedActionHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RequeueFailedActionHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var pk = body?.Pk?.Trim();
        var rk = body?.Rk?.Trim();

        if (string.IsNullOrWhiteSpace(pk) || string.IsNullOrWhiteSpace(rk))
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Pk and Rk required" });

        var delay = body?.DelayMinutes ?? 1;
        delay = Math.Clamp(delay, 0, 1440);

        var nextRetryUtc = DateTimeOffset.UtcNow.AddMinutes(delay);

        await _store.RequeueAsync(pk, rk, nextRetryUtc, ct);

        return await Json(req, HttpStatusCode.OK, new { ok = true, pk, rk, nextRetryUtc = nextRetryUtc.UtcDateTime.ToString("O") });
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }
}
