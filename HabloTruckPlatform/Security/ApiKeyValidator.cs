using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HabloTruckPlatform.Security;

public sealed class ApiKeyValidator : IApiKeyValidator
{
    private readonly ILogger<ApiKeyValidator> _logger;
    private readonly byte[]? _expectedApiKeyHash;
    private readonly bool _configured;
    private int _missingConfigLogged;

    public ApiKeyValidator(
        IOptions<HttpSecurityOptions> options,
        ILogger<ApiKeyValidator> logger)
    {
        _logger = logger;

        var expectedApiKey = options.Value.HttpApiKey?.Trim();
        _configured = !string.IsNullOrWhiteSpace(expectedApiKey);

        if (_configured)
        {
            _expectedApiKeyHash = Sha256(expectedApiKey!);
        }
        else
        {
            _expectedApiKeyHash = null;
        }
    }

    public ApiKeyValidationResult Validate(string? presentedApiKey)
    {
        if (!_configured || _expectedApiKeyHash is null)
        {
            if (Interlocked.Exchange(ref _missingConfigLogged, 1) == 0)
            {
                _logger.LogWarning(
                    "HTTP API key validation is enabled but HttpApiKey is not configured.");
            }

            return ApiKeyValidationResult.NotConfigured;
        }

        if (presentedApiKey is null)
            return ApiKeyValidationResult.Missing;

        var candidate = presentedApiKey.Trim();
        if (candidate.Length == 0)
            return ApiKeyValidationResult.Empty;

        var candidateHash = Sha256(candidate);
        var valid = CryptographicOperations.FixedTimeEquals(candidateHash, _expectedApiKeyHash);

        return valid
            ? ApiKeyValidationResult.Valid
            : ApiKeyValidationResult.Invalid;
    }

    private static byte[] Sha256(string value)
        => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
