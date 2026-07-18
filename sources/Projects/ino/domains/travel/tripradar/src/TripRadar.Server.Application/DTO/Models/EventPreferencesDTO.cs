using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class EventPreferencesDTO
{
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("noTraceMode")]
    public bool? NoTraceMode { get; init; }
}
