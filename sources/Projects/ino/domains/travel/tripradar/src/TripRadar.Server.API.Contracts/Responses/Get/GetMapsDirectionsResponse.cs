using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetMapsDirectionsResponse
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public MapsDirectionsSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("places_info")]
    public List<Dictionary<string, object>>? PlacesInfo { get; set; }

    [JsonPropertyName("directions")]
    public List<Dictionary<string, object>>? Directions { get; set; }

    [JsonPropertyName("durations")]
    public List<Dictionary<string, object>>? Durations { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
