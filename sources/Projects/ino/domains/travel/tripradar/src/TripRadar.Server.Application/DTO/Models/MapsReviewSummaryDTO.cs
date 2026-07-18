using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsReviewSummaryDTO
{
    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }
}
