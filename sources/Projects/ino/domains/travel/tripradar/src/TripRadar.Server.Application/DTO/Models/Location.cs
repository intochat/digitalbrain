using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class Location
{
    [JsonPropertyName("cityName")]
    public string CityName { get; set; } = string.Empty;
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    [JsonPropertyName("timezone")]
    public int Timezone { get; set; }
}
