using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetTripAdvisorSearchResponseDTO
{
    [JsonPropertyName("search_metadata")] public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public TripAdvisorSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("places")] public List<Dictionary<string, object>>? Places { get; set; }

    [JsonPropertyName("forums")] public List<Dictionary<string, object>>? Forums { get; set; }

    [JsonPropertyName("locations")] public List<Dictionary<string, object>>? Locations { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public Dictionary<string, object>? SerpapiPagination { get; set; }
}
