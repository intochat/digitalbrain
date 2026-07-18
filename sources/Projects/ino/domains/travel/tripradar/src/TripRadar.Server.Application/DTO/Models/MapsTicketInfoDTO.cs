using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class MapsTicketInfoDTO
{
    [JsonPropertyName("price")]
    public string? Price { get; set; }

    [JsonPropertyName("extracted_price")]
    public double? ExtractedPrice { get; set; }

    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_icon")]
    public string? SourceIcon { get; set; }
}
