using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record TierUsage(
        [property: JsonPropertyName("tierName")] string TierName,
        [property: JsonPropertyName("currentUsage")] int CurrentUsage,
        [property: JsonPropertyName("dailyLimit")] int DailyLimit,
        [property: JsonPropertyName("remainingRequests")] int RemainingRequests,
        [property: JsonPropertyName("usagePercentage")] double UsagePercentage
    );
}