using System.Net;
using System.Text.Json;
using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using HabloTruckPlatform.Functions.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace HabloTruckPlatform.Functions.Functions;

public sealed class CancelSubscriptionAtPeriodEndFunction
{
    private readonly CancelSubscriptionAtPeriodEndUseCase _useCase;

    public CancelSubscriptionAtPeriodEndFunction(CancelSubscriptionAtPeriodEndUseCase useCase)
    {
        _useCase = useCase;
    }

    [Function("CancelSubscriptionAtPeriodEnd")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/subscription/cancel-at-period-end")] HttpRequestData req,
        FunctionContext ctx)
    {
        CancelSubscriptionAtPeriodEndHttpRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<CancelSubscriptionAtPeriodEndHttpRequest>(
                req.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ctx.CancellationToken);
        }
        catch
        {
            return await Json(req, HttpStatusCode.BadRequest, new { ok = false, error = "Invalid JSON" });
        }

        var result = await _useCase.ExecuteAsync(new CancelSubscriptionAtPeriodEndRequest
        {
            Scope = body?.Scope ?? "individual",
            ActorUserPk = body?.ActorUserPk?.Trim() ?? string.Empty,
            ActorUserId = body?.ActorUserId?.Trim() ?? string.Empty,
            CompanyId = string.IsNullOrWhiteSpace(body?.CompanyId) ? null : body!.CompanyId!.Trim(),
            SubscriptionId = string.IsNullOrWhiteSpace(body?.SubscriptionId) ? null : body!.SubscriptionId!.Trim()
        }, ctx.CancellationToken);

        var status = result.Result
            ? HttpStatusCode.OK
            : ToStatusCode(result.Error);

        return await Json(req, status, new
        {
            ok = result.Result,
            scope = result.Scope,
            subscriptionId = result.SubscriptionId ?? "",
            cancelAtPeriodEnd = result.CancelAtPeriodEnd,
            alreadyScheduled = result.AlreadyScheduled,
            effectivePeriodEndUtc = result.EffectivePeriodEndUtc?.UtcDateTime.ToString("O") ?? "",
            currentPeriodEndUtc = result.CurrentPeriodEndUtc?.UtcDateTime.ToString("O") ?? "",
            cancelRequestedAtUtc = result.CancelRequestedAtUtc.UtcDateTime.ToString("O"),
            canceledAtUtc = result.CanceledAtUtc?.UtcDateTime.ToString("O") ?? "",
            error = result.Error
        });
    }

    private static HttpStatusCode ToStatusCode(string? error)
    {
        return error switch
        {
            "forbidden" => HttpStatusCode.Forbidden,
            "actor_not_found" => HttpStatusCode.NotFound,
            "company_not_found" => HttpStatusCode.NotFound,
            "subscription_not_found" => HttpStatusCode.NotFound,
            "stripe_update_failed" => HttpStatusCode.InternalServerError,
            _ => HttpStatusCode.BadRequest
        };
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode code, object obj)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(obj, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return res;
    }
}
