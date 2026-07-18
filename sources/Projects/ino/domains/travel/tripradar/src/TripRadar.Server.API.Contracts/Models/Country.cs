using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class Country
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("isoCode")]
    public string? IsoCode { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
