namespace TripRadar.Server.Infrastructure.Constants;

public static class SubscriptionConstants
{
    public static class Status
    {
        public const string Active = "active";
        public const string Trialing = "trialing";
        public const string PastDue = "past_due";
        public const string Unpaid = "unpaid";
    }
    
    public static class Metadata
    {
        public const string ProcessedByKey = "processed_by";
        public const string ProcessedAtKey = "processed_at";
        public const string ProcessedByValue = "internal_system";
    }
}
