using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class EventSearchParametersDTO
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = null!;

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = null!;
}
