using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsEditorialSummaryDTO
{
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }
}
