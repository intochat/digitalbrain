using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsExtractedSnippetDTO
{
    [JsonPropertyName("original")]
    public string? Original { get; set; }
}
