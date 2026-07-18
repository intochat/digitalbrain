using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetLocalPlacesResponse
{
    [JsonPropertyName("search_metadata")] public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public LocalSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("ads_results")] public List<LocalAdvertisementResult>? AdResults { get; set; }

    [JsonPropertyName("local_map")] public LocalMap? LocalMap { get; set; }

    [JsonPropertyName("local_results")] public List<LocalPlaceResult> LocalResults { get; set; } = new();

    [JsonPropertyName("discover_more_places")]
    public List<DiscoverMorePlace>? DiscoverMorePlaces { get; set; }

    [JsonPropertyName("pagination")] public LocalPagination? Pagination { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public LocalSerpApiPagination? SerpApiPagination { get; set; }
}