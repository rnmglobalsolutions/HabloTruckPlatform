namespace HabloTruckPlatform.Security;

public enum ApiKeyValidationResult
{
    Valid = 0,
    Missing = 1,
    Empty = 2,
    Invalid = 3,
    NotConfigured = 4
}
