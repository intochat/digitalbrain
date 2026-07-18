using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class NextFlights
{
    [JsonPropertyName("departureToken")]
    public string? DepartureToken { get; set; }
}
