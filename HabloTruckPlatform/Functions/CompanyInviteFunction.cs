using HabloTruckPlatform.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions;

public class CompanyInviteFunction
{
    private readonly ILogger<CompanyInviteFunction> _logger;
    private readonly IApiKeyValidator _apiKeyValidator;

    public CompanyInviteFunction(ILogger<CompanyInviteFunction> logger, IApiKeyValidator apiKeyValidator)
    {
        _logger = logger;
        _apiKeyValidator = apiKeyValidator;
    }

    [Function("CompanyInviteFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        var unauthorized = ApiKeyAuthorizationHelper.Authorize(req, _apiKeyValidator, _logger);
        if (unauthorized is not null)
            return unauthorized;

        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}
