using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class TripAdvisorPlaceSearchParametersDTO
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("place_id")] public string? PlaceId { get; set; }

    [JsonPropertyName("tripadvisor_domain")]
    public string? TripadvisorDomain { get; set; }
}
