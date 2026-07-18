using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class Airport
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }
}
