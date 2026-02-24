namespace HabloTruckPlatform.Infrastructure.ManyChat;

public sealed class ManyChatOptions
{
    public string BaseUrl { get; set; } = "https://api.manychat.com";
    public string ApiKey { get; set; } = "";

    // Tags (by name)
    public string TagAccessFull { get; set; } = "HT_ACCESS_FULL";
    public string TagAccessGrace { get; set; } = "HT_ACCESS_GRACE";
    public string TagAccessBlocked { get; set; } = "HT_ACCESS_BLOCKED";

    public string TagSourceIndividual { get; set; } = "HT_SRC_INDIVIDUAL";
    public string TagSourceCompany { get; set; } = "HT_SRC_COMPANY";

    // Optional: flow_ns for recovery
    public string? PaymentFailedFlowNs { get; set; }

    // Custom field names (by name)
    public string FieldAccessMode { get; set; } = "ht_access_mode";
    public string FieldGraceEndsAtUtc { get; set; } = "ht_grace_ends_utc";
    public string FieldCompanyId { get; set; } = "ht_company_id";
}