using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsAdmissionOptionDTO
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("extracted_price")]
    public double? ExtractedPrice { get; set; }

    [JsonPropertyName("official_site")]
    public bool? OfficialSite { get; set; }

    [JsonPropertyName("extensions")]
    public List<string>? Extensions { get; set; }
}
