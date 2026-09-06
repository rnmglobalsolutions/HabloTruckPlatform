using HabloTruckPlatform.Application.Models;

namespace HabloTruckPlatform.Infrastructure.Integrations.ManyChat;

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
    public string TagBillingActionRequired { get; set; } = "HT_BILLING_ACTION_REQUIRED";
    public string TagBillingRecovered { get; set; } = "HT_PAYMENT_RECOVERED";
    public string TagCancelScheduled { get; set; } = ManyChatLifecycleTags.CancelScheduled;
    public string TagChurned { get; set; } = ManyChatLifecycleTags.Churned;

    // Optional: flow_ns for recovery
    public string? PaymentFailedFlowNs { get; set; }
    public string? PaymentRecoveryReminderFlowNs { get; set; }

    // Optional: flow_ns for renewal/churn reminders
    public string? RenewalReminderFlowNs { get; set; }
    public string? SaveBeforeChurnFlowNs { get; set; }

    // Custom field names (by name)
    public string FieldAccessMode { get; set; } = "ht_access_mode";
    public string FieldGraceEndsAtUtc { get; set; } = "ht_grace_ends_utc";
    public string FieldCompanyId { get; set; } = "ht_company_id";
    public string FieldBillingRecoveryStatus { get; set; } = "ht_billing_recovery_status";
    public string FieldBillingRecoverySubscriptionId { get; set; } = "ht_billing_recovery_subscription_id";
    public string FieldBillingRecoveryInvoiceId { get; set; } = "ht_billing_recovery_invoice_id";
    public string FieldBillingRecoveryInvoiceStatus { get; set; } = "ht_billing_recovery_invoice_status";
    public string FieldBillingRecoveryUpdatedAtUtc { get; set; } = "ht_billing_recovery_updated_utc";
    public string FieldBillingRecoveryStartedAtUtc { get; set; } = "ht_billing_recovery_started_utc";

    // endpoint paths
    public string AddTagByNamePath { get; set; } = string.Empty;
    public string RemoveTagByNamePath { get; set; } = string.Empty;
    public string SetCustomFieldByNamePath { get; set; } = string.Empty;
    public string SendFlowPath { get; set; } = string.Empty;
}
