using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsPopularTimeSlotDTO
{
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("info")]
    public string? Info { get; set; }

    [JsonPropertyName("busyness_score")]
    public int? BusynessScore { get; set; }
}
