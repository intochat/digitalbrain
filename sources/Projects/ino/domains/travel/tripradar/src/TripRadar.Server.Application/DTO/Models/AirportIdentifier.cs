using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class AirportIdentifier
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
