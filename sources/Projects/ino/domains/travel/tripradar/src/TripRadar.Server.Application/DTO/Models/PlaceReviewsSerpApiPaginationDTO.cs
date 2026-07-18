using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsSerpApiPaginationDTO
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}
