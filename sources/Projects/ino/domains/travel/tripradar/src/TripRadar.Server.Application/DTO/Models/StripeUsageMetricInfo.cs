using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class StripeUsageMetricInfo
{
    [JsonPropertyName("used")]
    public decimal? Used { get; set; }

    [JsonPropertyName("limit")]
    public decimal? Limit { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }
}
