using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class VenueDTO
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("reviews")]
    public int? Reviews { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; } = null!;
}
