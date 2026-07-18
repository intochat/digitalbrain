using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetYelpPlaceResponse
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public YelpPlaceSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("place_results")]
    public Dictionary<string, object>? PlaceResults { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
