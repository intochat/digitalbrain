using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetTripAdvisorSearchResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public TripAdvisorSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("places")] public List<Dictionary<string, object>>? Places { get; set; }

    [JsonPropertyName("forums")] public List<Dictionary<string, object>>? Forums { get; set; }

    [JsonPropertyName("locations")] public List<Dictionary<string, object>>? Locations { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public Dictionary<string, object>? SerpapiPagination { get; set; }
}
