using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class FlightPreferences
{
    [JsonPropertyName("Adults")]
    [DataMember(Name = "Adults")]
    [Range(1, 10, ErrorMessage = "Adults must be between 1 and 10.")]
    public int? Adults { get; set; }

    [JsonPropertyName("Children")]
    [DataMember(Name = "Children")]
    [Range(0, 10, ErrorMessage = "Children must be between 0 and 10.")]
    public int? Children { get; set; }

    [JsonPropertyName("InfantsInSeat")]
    [DataMember(Name = "InfantsInSeat")]
    [Range(0, 10, ErrorMessage = "InfantsInSeat must be between 0 and 10.")]
    public int? InfantsInSeat { get; set; }

    [JsonPropertyName("InfantsOnLap")]
    [DataMember(Name = "InfantsOnLap")]
    [Range(0, 10, ErrorMessage = "InfantsOnLap must be between 0 and 10.")]
    public int? InfantsOnLap { get; set; }

    [JsonPropertyName("MaxPrice")]
    [DataMember(Name = "MaxPrice")]
    [Range(1, 1000000, ErrorMessage = "MaxPrice must be between 1 and 1,000,000.")]
    public int? MaxPrice { get; set; }

    [JsonPropertyName("Currency")]
    [DataMember(Name = "Currency")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be a 3-character currency code.")]
    public string? Currency { get; set; }

    [JsonPropertyName("TravelClass")]
    [DataMember(Name = "TravelClass")]
    public TravelClassType? TravelClass { get; set; }

    [JsonPropertyName("SortBy")]
    [DataMember(Name = "SortBy")]
    public FlightSortByType? SortBy { get; set; }

    [JsonPropertyName("PreferredDepartureAirportCode")]
    [DataMember(Name = "PreferredDepartureAirportCode")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "PreferredDepartureAirportCode must be a valid 3-letter IATA code.")]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "PreferredDepartureAirportCode must be a valid 3-letter IATA code in uppercase.")]
    public string? PreferredDepartureAirportCode { get; set; }

    [JsonPropertyName("PreferredAirlines")]
    [DataMember(Name = "PreferredAirlines")]
    public string[]? PreferredAirlines { get; set; }

    [JsonPropertyName("NoTraceMode")]
    [DataMember(Name = "NoTraceMode")]
    public bool? NoTraceMode { get; set; }

    [JsonPropertyName("DeepSearch")]
    [DataMember(Name = "DeepSearch")]
    public bool? DeepSearch { get; set; }
}

