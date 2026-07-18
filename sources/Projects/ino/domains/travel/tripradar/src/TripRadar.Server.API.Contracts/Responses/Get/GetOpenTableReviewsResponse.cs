using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetOpenTableReviewsResponse
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public OpenTableSearchParameters SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public OpenTableSearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("reviews_summary")]
    public OpenTableReviewsSummary? ReviewsSummary { get; set; }

    [JsonPropertyName("awards")]
    public List<OpenTableAward>? Awards { get; set; }

    [JsonPropertyName("reviews")]
    public List<OpenTableReview>? Reviews { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public OpenTableSerpApiPagination? SerpApiPagination { get; set; }

    [JsonPropertyName("error")] public string? Error { get; set; }
}
