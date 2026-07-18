using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class AirportIdentifier
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }
}
