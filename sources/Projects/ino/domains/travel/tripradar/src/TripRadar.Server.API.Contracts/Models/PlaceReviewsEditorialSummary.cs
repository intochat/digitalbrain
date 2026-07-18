using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceReviewsEditorialSummary
{
    [JsonPropertyName("overview")] public string? Overview { get; set; }
}
