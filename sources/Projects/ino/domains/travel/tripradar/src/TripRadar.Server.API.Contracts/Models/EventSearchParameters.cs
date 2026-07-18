using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class EventSearchParameters
{
    [JsonPropertyName("q")] public string? Query { get; set; }

    [JsonPropertyName("engine")] public string? Engine { get; set; }
}
