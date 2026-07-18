using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PointOfInterestDTO
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("location")]
    public GpsCoordinatesDTO Location { get; set; } = null!;
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();
    [JsonPropertyName("rating")]
    public int Rating { get; set; }
    [JsonPropertyName("address")]
    public string? Address { get; set; }
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; set; }
    [JsonPropertyName("wikipediaUrl")]
    public string? WikipediaUrl { get; set; }
    [JsonPropertyName("wikidataId")]
    public string? WikidataId { get; set; }
    [JsonPropertyName("details")]
    public PointOfInterestDetailsDTO? Details { get; set; }
}
