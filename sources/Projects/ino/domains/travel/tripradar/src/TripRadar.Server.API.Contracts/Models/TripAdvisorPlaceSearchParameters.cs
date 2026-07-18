using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class TripAdvisorPlaceSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }
}
