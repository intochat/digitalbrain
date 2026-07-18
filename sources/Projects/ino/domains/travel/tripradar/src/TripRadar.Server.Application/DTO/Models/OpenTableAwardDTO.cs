using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class OpenTableAwardDTO
{
    [JsonPropertyName("location")] public string? Location { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }
}
