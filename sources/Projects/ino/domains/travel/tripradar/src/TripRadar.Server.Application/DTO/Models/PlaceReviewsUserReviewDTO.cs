using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsUserReviewDTO
{
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }
}
