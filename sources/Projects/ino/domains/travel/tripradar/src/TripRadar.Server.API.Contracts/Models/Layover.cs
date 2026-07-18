using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Layover
{
    [JsonPropertyName("duration")] public int Duration { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = null!;

    [JsonPropertyName("id")] public string Id { get; set; } = null!;
}
