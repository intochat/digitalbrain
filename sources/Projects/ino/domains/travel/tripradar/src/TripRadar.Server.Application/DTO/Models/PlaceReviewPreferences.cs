using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class PlaceReviewPreferencesDTO
{
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonPropertyName("sortBy")]
    public string? SortBy { get; init; }
}
