using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public sealed class UsageMetricResponse
{
    [JsonPropertyName("used")]
    [DataMember(Name = "used")]
    public decimal? Used { get; set; }

    [JsonPropertyName("limit")]
    [DataMember(Name = "limit")]
    public decimal? Limit { get; set; }

    [JsonPropertyName("unit")]
    [DataMember(Name = "unit")]
    public string? Unit { get; set; }
}
