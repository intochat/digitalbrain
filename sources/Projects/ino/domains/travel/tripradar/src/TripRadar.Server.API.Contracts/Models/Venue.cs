using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Venue
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("rating")] public double? Rating { get; set; }

    [JsonPropertyName("reviews")] public int? Reviews { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }
}
