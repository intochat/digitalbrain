using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class EventLocationMap
{
    [JsonPropertyName("image")] public string? Image { get; set; }

    [JsonPropertyName("link")] public string? Link { get; set; }

    [JsonPropertyName("serpapi_link")] public string? SerpapiLink { get; set; }
}
