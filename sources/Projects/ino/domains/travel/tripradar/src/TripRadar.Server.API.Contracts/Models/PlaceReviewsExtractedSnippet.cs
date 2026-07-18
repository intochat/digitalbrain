using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsExtractedSnippet
{
    [JsonPropertyName("original")] public string? Original { get; set; }
}
