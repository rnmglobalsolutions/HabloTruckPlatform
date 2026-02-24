using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions;

public class StripeWebhookFunction
{
    private readonly ILogger<StripeWebhookFunction> _logger;

    public StripeWebhookFunction(ILogger<StripeWebhookFunction> logger)
    {
        _logger = logger;
    }

    [Function("StripeWebhookFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}