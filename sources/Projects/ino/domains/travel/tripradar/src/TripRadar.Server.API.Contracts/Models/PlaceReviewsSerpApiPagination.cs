using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsSerpApiPagination
{
    [JsonPropertyName("next")] public string? Next { get; set; }

    [JsonPropertyName("next_page_token")] public string? NextPageToken { get; set; }
}
