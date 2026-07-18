using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class GeographicLocation
{
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("uule")]
    public string? Uule { get; set; }
}
