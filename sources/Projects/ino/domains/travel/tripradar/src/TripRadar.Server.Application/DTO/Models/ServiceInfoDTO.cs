using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class ServiceInfoDTO
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}
