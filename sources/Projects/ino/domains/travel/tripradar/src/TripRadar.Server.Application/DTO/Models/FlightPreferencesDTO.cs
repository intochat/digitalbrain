using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class FlightPreferencesDTO
{
    [JsonPropertyName("adults")]
    public int? Adults { get; init; }

    [JsonPropertyName("children")]
    public int? Children { get; init; }

    [JsonPropertyName("infantsInSeat")]
    public int? InfantsInSeat { get; init; }

    [JsonPropertyName("infantsOnLap")]
    public int? InfantsOnLap { get; init; }

    [JsonPropertyName("maxPrice")]
    public int? MaxPrice { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("travelClass")]
    public string? TravelClass { get; init; }

    [JsonPropertyName("sortBy")]
    public string? SortBy { get; init; }

    [JsonPropertyName("preferredDepartureAirportCode")]
    public string? PreferredDepartureAirportCode { get; init; }

    [JsonPropertyName("preferredAirlines")]
    public string[]? PreferredAirlines { get; init; }

    [JsonPropertyName("noTraceMode")]
    public bool? NoTraceMode { get; init; }

    [JsonPropertyName("deepSearch")]
    public bool? DeepSearch { get; init; }
}

