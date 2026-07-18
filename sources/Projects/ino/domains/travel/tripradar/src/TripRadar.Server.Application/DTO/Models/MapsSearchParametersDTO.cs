using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsSearchParametersDTO
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("place_id")]
    public string? PlaceId { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("google_domain")]
    public string GoogleDomain { get; set; } = string.Empty;

    [JsonPropertyName("hl")]
    public string Hl { get; set; } = "en";

    [JsonPropertyName("gl")]
    public string Gl { get; set; } = "us";
}
