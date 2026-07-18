using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class StripeUsageSummaryInfo
{
    [JsonPropertyName("hasMeteredBilling")]
    public bool HasMeteredBilling { get; set; }

    [JsonPropertyName("currentPeriod")]
    public StripeBillingPeriodInfo CurrentPeriod { get; set; } = new();

    [JsonPropertyName("usage")]
    public Dictionary<string, StripeUsageMetricInfo> Usage { get; set; } = new();
}
