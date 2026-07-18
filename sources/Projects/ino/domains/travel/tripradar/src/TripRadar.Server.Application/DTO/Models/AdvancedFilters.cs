using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class AdvancedFilters
{
    [Preference(nameof(PreferenceType.MaxLayovers))]
    [JsonPropertyName("stops")]
    public StopsType? Stops { get; set; }

    [Preference(nameof(PreferenceType.AvoidAirlines))]
    [JsonPropertyName("excludeAirlines")]
    public string? ExcludeAirlines { get; set; }

    [Preference(nameof(PreferenceType.PreferredAirlines))]
    [JsonPropertyName("includeAirlines")]
    public string? IncludeAirlines { get; set; }

    [JsonPropertyName("bags")]
    public int? Bags { get; set; }

    [Preference(nameof(PreferenceType.MaxPrice))]
    [JsonPropertyName("maxPrice")]
    public int? MaxPrice { get; set; }

    [Preference(nameof(PreferenceType.PreferredDepartureTime))]
    [JsonPropertyName("outboundTimes")]
    public string? OutboundTimes { get; set; }

    [Preference(nameof(PreferenceType.PreferredArrivalTime))]
    [JsonPropertyName("returnTimes")]
    public string? ReturnTimes { get; set; }

    [JsonPropertyName("emissions")]
    public int? Emissions { get; set; }

    [JsonPropertyName("layoverDuration")]
    public string? LayoverDuration { get; set; }

    [JsonPropertyName("maxDuration")]
    public int? MaxDuration { get; set; }
}
