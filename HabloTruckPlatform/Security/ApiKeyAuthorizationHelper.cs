using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace HabloTruckPlatform.Security;

public static class ApiKeyAuthorizationHelper
{
    public const string ApiKeyHeaderName = "x-api-key";

    public static async Task<HttpResponseData?> AuthorizeAsync(
        HttpRequestData request,
        IApiKeyValidator validator,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(ReadHeader(request.Headers));
        if (validation == ApiKeyValidationResult.Valid)
            return null;

        logger.LogInformation(
            "HTTP authentication failed. Reason={Reason}",
            validation);

        var response = request.CreateResponse(HttpStatusCode.Unauthorized);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(
            "{\"ok\":false,\"error\":\"Unauthorized\"}",
            cancellationToken);

        return response;
    }

    public static IActionResult? Authorize(
        HttpRequest request,
        IApiKeyValidator validator,
        ILogger logger)
    {
        var validation = validator.Validate(ReadHeader(request.Headers));
        if (validation == ApiKeyValidationResult.Valid)
            return null;

        logger.LogInformation(
            "HTTP authentication failed. Reason={Reason}",
            validation);

        return new UnauthorizedObjectResult(new
        {
            ok = false,
            error = "Unauthorized"
        });
    }

    private static string? ReadHeader(HttpHeadersCollection headers)
    {
        if (!headers.TryGetValues(ApiKeyHeaderName, out var values))
            return null;

        return values.FirstOrDefault();
    }

    private static string? ReadHeader(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(ApiKeyHeaderName, out var values))
            return null;

        return values.FirstOrDefault();
    }
}
