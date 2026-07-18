using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class GpsCoordinatesDTO
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}
