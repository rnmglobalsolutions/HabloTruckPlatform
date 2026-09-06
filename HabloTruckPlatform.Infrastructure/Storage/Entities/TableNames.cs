namespace HabloTruckPlatform.Infrastructure.Storage.Entities;

public static class TableNames
{
    public const string Users = "HTUsers";
    public const string UserEmail = "HTUserEmail";
    public const string UserPhone = "HTUserPhone";
    public const string UserManyChat = "HTUserManyChat";
    public const string UserStripeCustomer = "HTUserStripeCustomer";
    public const string ExternalIdentities = "HTExternalIdentities";
    public const string ExternalIdentityLookup = "HTExternalIdentityLookup";
    public const string GraceIndex = "HTGraceIndex";
    public const string StripeEvents = "HTStripeEvents";
    public const string Companies = "HTCompanies";
    public const string Entitlements = "HTEntitlements";
    public const string EntitlementExpiryIndex = "HTEntitlementExpiryIndex";
    public const string Seats = "HTSeats";
    public const string InviteCodes = "HTInviteCodes";
    public const string InviteCompanyIndex = "HTInviteCompanyIndex";
    public const string FailedActions = "HTFailedActions";
    public const string StripeEventAudit = "HTStripeEventAudit";
    public const string SubscriptionReminders = "HTSubscriptionReminders";
    public const string JobCheckpoints = "HTJobCheckpoints";
}
