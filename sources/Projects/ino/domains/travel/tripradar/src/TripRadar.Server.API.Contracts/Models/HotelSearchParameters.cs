using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class HotelSearchParameters
{
    [JsonPropertyName("engine")] public string? Engine { get; set; }

    [JsonPropertyName("q")] public string? Query { get; set; }

    [JsonPropertyName("gl")] public string? CountryCode { get; set; }

    [JsonPropertyName("hl")] public string? LanguageCode { get; set; }

    [JsonPropertyName("currency")] public string? Currency { get; set; }

    [JsonPropertyName("check_in_date")] public string? CheckInDate { get; set; }

    [JsonPropertyName("check_out_date")] public string? CheckOutDate { get; set; }

    [JsonPropertyName("adults")] public int Adults { get; set; }

    [JsonPropertyName("children")] public int Children { get; set; }
}
