namespace HabloTruckPlatform.Security;

public interface IApiKeyValidator
{
    ApiKeyValidationResult Validate(string? presentedApiKey);
}
