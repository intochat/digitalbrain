using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class AirportDetail
{
    [JsonPropertyName("airport")]
    public AirportIdentifier? Airport { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }
}
