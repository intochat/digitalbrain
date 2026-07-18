using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsWeatherDTO
{
    [JsonPropertyName("celsius")]
    public string? Celsius { get; set; }

    [JsonPropertyName("fahrenheit")]
    public string? Fahrenheit { get; set; }

    [JsonPropertyName("conditions")]
    public string? Conditions { get; set; }
}
