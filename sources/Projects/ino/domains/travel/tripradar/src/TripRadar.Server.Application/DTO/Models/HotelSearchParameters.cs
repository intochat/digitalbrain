using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class HotelSearchParameters
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("check_in_date")]
    public string? CheckInDate { get; set; }

    [JsonPropertyName("check_out_date")]
    public string? CheckOutDate { get; set; }

    [JsonPropertyName("adults")]
    public int Adults { get; set; }

    [JsonPropertyName("children")]
    public int Children { get; set; }
}
