using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsPaginationDTO
{
    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public PlaceReviewsSerpApiPaginationDTO? SerpApiPagination { get; set; }
}
