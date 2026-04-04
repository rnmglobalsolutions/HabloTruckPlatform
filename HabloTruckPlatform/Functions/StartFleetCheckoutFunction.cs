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

public sealed class StartFleetCheckoutFunction
{
    private readonly StartFleetCheckoutUseCase _useCase;
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<StartFleetCheckoutFunction> _logger;

    public StartFleetCheckoutFunction(
        StartFleetCheckoutUseCase useCase,
        IApiKeyValidator apiKeyValidator,
        ILogger<StartFleetCheckoutFunction> logger)
    {
        _useCase = useCase;
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("StartFleetCheckout")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "company/fleet/start-checkout")] HttpRequestData req,
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

        StartFleetCheckoutHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<StartFleetCheckoutHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ct);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var result = await _useCase.ExecuteAsync(new StartFleetCheckoutRequest
        {
            CompanyId = body?.CompanyId,
            CompanyName = body?.CompanyName,
            Email = body?.Email,
            PhoneE164 = body?.PhoneE164,
            ManyChatSubscriberId = body?.ManyChatSubscriberId,
            ManyChatChannel = body?.ManyChatChannel,
            Seats = body?.Seats ?? 0,
            SuccessUrl = body?.SuccessUrl,
            CancelUrl = body?.CancelUrl
        }, ct);

        var status = result.Result ? HttpStatusCode.OK : HttpStatusCode.BadRequest;

        return await Json(req, status, new
        {
            ok = result.Result,
            error = result.Error,
            companyId = result.CompanyId,
            companyName = result.CompanyName,
            seats = result.Seats,
            url = result.Url,
            sessionId = result.SessionId
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
