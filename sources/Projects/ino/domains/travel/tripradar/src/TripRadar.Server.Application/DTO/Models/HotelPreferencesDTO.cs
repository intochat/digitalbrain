using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class HotelPreferencesDTO
{
    [JsonPropertyName("adults")]
    public int? Adults { get; init; }

    [JsonPropertyName("children")]
    public int? Children { get; init; }

    [JsonPropertyName("minPrice")]
    public int? MinPrice { get; init; }

    [JsonPropertyName("maxPrice")]
    public int? MaxPrice { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("sortBy")]
    public string? SortBy { get; init; }

    [JsonPropertyName("freeCancellation")]
    public bool? FreeCancellation { get; init; }

    [JsonPropertyName("rating")]
    public string? Rating { get; init; }

    [JsonPropertyName("noTraceMode")]
    public bool? NoTraceMode { get; init; }
}
