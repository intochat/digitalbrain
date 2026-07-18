using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class PointOfInterestDetailsDTO
{
    [JsonPropertyName("city")]
    public string? City { get; set; }
    [JsonPropertyName("state")]
    public string? State { get; set; }
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    [JsonPropertyName("countryCode")]
    public string? CountryCode { get; set; }
    [JsonPropertyName("postcode")]
    public string? Postcode { get; set; }
    [JsonPropertyName("road")]
    public string? Road { get; set; }
    [JsonPropertyName("houseNumber")]
    public string? HouseNumber { get; set; }
    [JsonPropertyName("openingHours")]
    public string? OpeningHours { get; set; }
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    [JsonPropertyName("website")]
    public string? Website { get; set; }
}
