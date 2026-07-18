using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class FlightSegment
{
    [Required]
    [JsonPropertyName("departure_airport")]
    public Airport DepartureAirport { get; set; } = null!;

    [Required]
    [JsonPropertyName("arrival_airport")]
    public Airport ArrivalAirport { get; set; } = null!;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [Required]
    [JsonPropertyName("airplane")]
    public string Airplane { get; set; } = null!;

    [Required]
    [JsonPropertyName("airline")]
    public string Airline { get; set; } = null!;

    [Required]
    [JsonPropertyName("airline_logo")]
    public string AirlineLogo { get; set; } = null!;

    [Required]
    [JsonPropertyName("travel_class")]
    public string TravelClass { get; set; } = null!;

    [Required]
    [JsonPropertyName("flight_number")]
    public string FlightNumber { get; set; } = null!;

    [JsonPropertyName("ticket_also_sold_by")]
    public List<string>? TicketAlsoSoldBy { get; set; }

    [Required]
    [JsonPropertyName("legroom")]
    public string Legroom { get; set; } = null!;

    [Required]
    [JsonPropertyName("extensions")]
    public List<string> Extensions { get; set; } = null!;

    [JsonPropertyName("plane_and_crew_by")]
    public string? PlaneAndCrewBy { get; set; }
}
