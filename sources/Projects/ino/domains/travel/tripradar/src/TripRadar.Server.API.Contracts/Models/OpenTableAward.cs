using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class OpenTableAward
{
    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }
}
