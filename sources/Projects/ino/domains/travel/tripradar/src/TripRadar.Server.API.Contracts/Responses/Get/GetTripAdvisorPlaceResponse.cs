using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetTripAdvisorPlaceResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public TripAdvisorPlaceSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("place_result")]
    public Dictionary<string, object>? PlaceResult { get; set; }
}
