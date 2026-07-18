using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetLocalPlacesResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public LocalSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("ad_results")]
    public List<LocalAdvertisementResultDTO>? AdResults { get; set; }

    [JsonPropertyName("local_map")]
    public LocalMapDTO? LocalMap { get; set; }

    [JsonPropertyName("local_results")]
    public List<LocalPlaceResultDTO> LocalResults { get; set; } = new();

    [JsonPropertyName("discover_more_places")]
    public List<DiscoverMorePlaceDTO>? DiscoverMorePlaces { get; set; }

    [JsonPropertyName("pagination")]
    public LocalPaginationDTO? Pagination { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public LocalSerpApiPaginationDTO? SerpApiPagination { get; set; }
}
