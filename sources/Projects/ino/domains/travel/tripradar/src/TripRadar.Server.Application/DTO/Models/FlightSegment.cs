using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class FlightSegment
{
    [JsonPropertyName("departure_airport")]
    public Airport DepartureAirport { get; set; } = null!;

    [JsonPropertyName("arrival_airport")]
    public Airport ArrivalAirport { get; set; } = null!;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("airplane")]
    public string Airplane { get; set; } = null!;

    [JsonPropertyName("airline")]
    public string Airline { get; set; } = null!;

    [JsonPropertyName("airline_logo")]
    public string AirlineLogo { get; set; } = null!;

    [JsonPropertyName("travel_class")]
    public string TravelClass { get; set; } = null!;

    [JsonPropertyName("flight_number")]
    public string FlightNumber { get; set; } = null!;

    [JsonPropertyName("ticket_also_sold_by")]
    public List<string>? TicketAlsoSoldBy { get; set; }

    [JsonPropertyName("legroom")]
    public string Legroom { get; set; } = null!;

    [JsonPropertyName("extensions")]
    public List<string> Extensions { get; set; } = null!;

    [JsonPropertyName("plane_and_crew_by")]
    public string? PlaneAndCrewBy { get; set; }
}
