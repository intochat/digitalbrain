using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetYelpSearchResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public YelpSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("filters")]
    public Dictionary<string, object>? Filters { get; set; }

    [JsonPropertyName("ads_results")]
    public List<Dictionary<string, object>>? AdsResults { get; set; }

    [JsonPropertyName("organic_results")]
    public List<Dictionary<string, object>>? OrganicResults { get; set; }

    [JsonPropertyName("pagination")]
    public Dictionary<string, object>? Pagination { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public Dictionary<string, object>? SerpApiPagination { get; set; }
}
