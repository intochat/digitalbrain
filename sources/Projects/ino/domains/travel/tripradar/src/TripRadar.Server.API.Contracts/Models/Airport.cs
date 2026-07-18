using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Airport
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("id")] public string? Code { get; set; }

    [JsonPropertyName("time")] public string? Time { get; set; }
}
