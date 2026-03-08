using HabloTruckPlatform.Application.Models;
using HabloTruckPlatform.Application.UseCases;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace HabloTruckPlatform.Functions.Functions;

public class StripePayment_GetPaymentLink
{
    private readonly StripeCheckoutHandler _stripeCheckoutHandler;
    private readonly ILogger<StripePayment_GetPaymentLink> _logger;

    public StripePayment_GetPaymentLink(
        StripeCheckoutHandler stripeCheckoutHandler,
        ILogger<StripePayment_GetPaymentLink> logger)
    {
        _stripeCheckoutHandler = stripeCheckoutHandler;
        _logger = logger;
    }

    [Function("StripeGetPaymentLink")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "stripe/payment-link")] HttpRequestData req,
        FunctionContext ctx)
    {
        StripeCheckoutSessionRequest? input;
        string requestBody = string.Empty;
        string errorMessage = string.Empty;

        try
        {
            try
            {
                input = await JsonSerializer.DeserializeAsync<StripeCheckoutSessionRequest>(
                    req.Body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    ctx.CancellationToken);
            }
            catch
            {
                var badJson = req.CreateResponse(HttpStatusCode.BadRequest);
                await badJson.WriteAsJsonAsync(new StripeCheckoutSessionResult
                {
                    Result = false,
                    Url = "",
                    SessionId = null,
                    Error = "invalid_json_body"
                }, cancellationToken: ctx.CancellationToken);

                return badJson;
            }

            var result = await _stripeCheckoutHandler.ExecuteAsync(
                input ?? new StripeCheckoutSessionRequest(),
                ctx.CancellationToken);

            var response = req.CreateResponse(result.Result ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(result, cancellationToken: ctx.CancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            var exMessage = ex.Message + " - " + ex.InnerException?.Message;
            _logger.LogError(
                ex,
                "StripePayment_GetPaymentLink - Unhandled error: {exMessage}",
                exMessage);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);

            await errorResponse.WriteAsJsonAsync(new StripeCheckoutSessionResult
            {
                Result = false,
                Url = "",
                SessionId = null,
                Error = "internal_server_error"
            }, cancellationToken: ctx.CancellationToken);

            return errorResponse;
        }
    }
}