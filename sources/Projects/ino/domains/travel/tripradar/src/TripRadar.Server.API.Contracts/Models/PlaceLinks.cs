using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class PlaceLinks
{
    [JsonPropertyName("phone")] public string? Phone { get; set; }

    [JsonPropertyName("directions")] public string? Directions { get; set; }

    [JsonPropertyName("website")] public string? Website { get; set; }

    [JsonPropertyName("order")] public string? Order { get; set; }
}
