using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class Layover
{
    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;
}
