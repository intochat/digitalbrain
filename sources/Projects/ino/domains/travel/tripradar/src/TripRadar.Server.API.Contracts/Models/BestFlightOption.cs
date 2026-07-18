using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class BestFlightOption
{
    [JsonPropertyName("flights")] public List<FlightSegment> Flights { get; set; } = null!;

    [JsonPropertyName("layovers")] public List<Layover> Layovers { get; set; } = null!;

    [JsonPropertyName("total_duration")] public int TotalDuration { get; set; }

    [JsonPropertyName("carbon_emissions")] public CarbonEmissions CarbonEmissions { get; set; } = null!;

    [JsonPropertyName("price")] public decimal Price { get; set; }

    [JsonPropertyName("type")] public string Type { get; set; } = null!;

    [JsonPropertyName("airline_logo")] public string AirlineLogo { get; set; } = null!;

    [JsonPropertyName("booking_token")] public string BookingToken { get; set; } = null!;
}
