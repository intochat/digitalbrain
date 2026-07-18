using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class BookingFlights
{
    [JsonPropertyName("bookingToken")]
    public string? BookingToken { get; set; }
}
