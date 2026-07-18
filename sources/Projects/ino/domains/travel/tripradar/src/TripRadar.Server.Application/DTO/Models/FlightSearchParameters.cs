using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class FlightSearchParameters
{
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = null!;

    [JsonPropertyName("hl")]
    public string LanguageCode { get; set; } = null!;

    [JsonPropertyName("gl")]
    public string CountryCode { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("departure_id")]
    public string DepartureId { get; set; } = null!;

    [JsonPropertyName("arrival_id")]
    public string ArrivalId { get; set; } = null!;

    [JsonPropertyName("outbound_date")]
    public string OutboundDate { get; set; } = null!;

    [JsonPropertyName("adults")]
    public int Adults { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("travel_class")]
    public int? TravelClass { get; set; }
}
