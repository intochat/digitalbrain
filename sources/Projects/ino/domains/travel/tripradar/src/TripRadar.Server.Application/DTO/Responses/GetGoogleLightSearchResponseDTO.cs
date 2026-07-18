using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetGoogleLightSearchResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public GoogleLightSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("organic_results")]
    public List<Dictionary<string, object>>? OrganicResults { get; set; }

    [JsonPropertyName("related_searches")]
    public List<Dictionary<string, object>>? RelatedSearches { get; set; }

    [JsonPropertyName("pagination")]
    public Dictionary<string, object>? Pagination { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public Dictionary<string, object>? SerpApiPagination { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}
