using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetMapsPlaceResultsResponse
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public MapsPlaceResultsSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("place_results")]
    public Dictionary<string, object>? PlaceResults { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
