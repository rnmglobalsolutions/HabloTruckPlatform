using HabloTruckPlatform.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;

public class HealthFunction
{
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<HealthFunction> _logger;

    public HealthFunction(IApiKeyValidator apiKeyValidator, ILogger<HealthFunction> logger)
    {
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        FunctionContext ctx)
    {
        var unauthorized = await ApiKeyAuthorizationHelper.AuthorizeAsync(
            req,
            _apiKeyValidator,
            _logger,
            ctx.CancellationToken);

        if (unauthorized is not null)
            return unauthorized;

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("OK");
        return response;
    }
}
