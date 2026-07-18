using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class HotelBooking
{
    [JsonPropertyName("propertyToken")]
    public string? PropertyToken { get; set; }
}
