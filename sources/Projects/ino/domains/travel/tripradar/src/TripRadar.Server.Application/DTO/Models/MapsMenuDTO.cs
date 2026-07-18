using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsMenuDTO
{
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}
