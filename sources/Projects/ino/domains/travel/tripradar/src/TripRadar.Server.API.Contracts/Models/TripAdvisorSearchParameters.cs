using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class TripAdvisorSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("q")] public string? Q { get; set; }

    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }

    [JsonPropertyName("ssrc")] public string? Ssrc { get; set; }

    [JsonPropertyName("offset")] public int? Offset { get; set; }

    [JsonPropertyName("limit")] public int? Limit { get; set; }

    [JsonPropertyName("lat")] public double? Lat { get; set; }

    [JsonPropertyName("lon")] public double? Lon { get; set; }
}
