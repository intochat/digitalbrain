using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsPaginationResult
{
    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public PlaceReviewsSerpApiPagination? SerpApiPagination { get; set; }
}
