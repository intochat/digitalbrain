using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetYelpReviewsResponse
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public YelpReviewsSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("review_languages")]
    public List<Dictionary<string, object>>? ReviewLanguages { get; set; }

    [JsonPropertyName("reviews")]
    public List<Dictionary<string, object>>? Reviews { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public Dictionary<string, object>? SerpApiPagination { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
