using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsUserReview
{
    [JsonPropertyName("rating")] public double? Rating { get; set; }

    [JsonPropertyName("snippet")] public string? Snippet { get; set; }
}
