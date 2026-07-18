using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class LocalMap
{
    [JsonPropertyName("image")] public string? Image { get; set; }
}
