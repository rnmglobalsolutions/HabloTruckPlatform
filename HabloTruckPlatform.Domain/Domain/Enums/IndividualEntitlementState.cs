public enum IndividualEntitlementState
{
    None = 0,
    Active = 1,       // active/trialing, not ended
    PaidThrough = 2,  // current_period_end in the future
    Grace = 3,
    Blocked = 4
}