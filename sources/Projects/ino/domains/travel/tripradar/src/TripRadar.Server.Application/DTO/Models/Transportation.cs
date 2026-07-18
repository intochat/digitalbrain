using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class Transportation
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }
}
