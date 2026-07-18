using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

/// <summary>
/// Search parameters returned in Flight Explore API response.
/// </summary>
public class FlightExploreSearchParameters
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("departure_id")]
    public string? DepartureId { get; set; }

    [JsonPropertyName("arrival_id")]
    public string? ArrivalId { get; set; }

    [JsonPropertyName("arrival_area_id")]
    public string? ArrivalAreaId { get; set; }

    [JsonPropertyName("gl")]
    public string? Gl { get; set; }

    [JsonPropertyName("hl")]
    public string? Hl { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("type")]
    public int? Type { get; set; }

    [JsonPropertyName("outbound_date")]
    public string? OutboundDate { get; set; }

    [JsonPropertyName("return_date")]
    public string? ReturnDate { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("travel_duration")]
    public int? TravelDuration { get; set; }

    [JsonPropertyName("travel_class")]
    public int? TravelClass { get; set; }

    [JsonPropertyName("adults")]
    public int? Adults { get; set; }

    [JsonPropertyName("children")]
    public int? Children { get; set; }

    [JsonPropertyName("infants_in_seat")]
    public int? InfantsInSeat { get; set; }

    [JsonPropertyName("infants_on_lap")]
    public int? InfantsOnLap { get; set; }

    [JsonPropertyName("stops")]
    public int? Stops { get; set; }

    [JsonPropertyName("travel_mode")]
    public int? TravelMode { get; set; }

    [JsonPropertyName("interest")]
    public string? Interest { get; set; }

    [JsonPropertyName("include_airlines")]
    public string? IncludeAirlines { get; set; }

    [JsonPropertyName("bags")]
    public int? Bags { get; set; }

    [JsonPropertyName("max_price")]
    public int? MaxPrice { get; set; }

    [JsonPropertyName("max_duration")]
    public int? MaxDuration { get; set; }
}
