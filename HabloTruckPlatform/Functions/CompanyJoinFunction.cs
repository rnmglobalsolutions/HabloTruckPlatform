using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Functions;

public class CompanyJoinFunction
{
    private readonly ILogger<CompanyJoinFunction> _logger;

    public CompanyJoinFunction(ILogger<CompanyJoinFunction> logger)
    {
        _logger = logger;
    }

    [Function("CompanyJoinFunction")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}