using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class Price
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }

    [JsonPropertyName("num_guests")]
    public int NumGuests { get; set; }

    [JsonPropertyName("rate_per_night")]
    public Rate? RatePerNight { get; set; }

    [JsonPropertyName("free_cancellation")]
    public bool? FreeCancellation { get; set; }

    [JsonPropertyName("free_cancellation_until_date")]
    public string? FreeCancellationUntilDate { get; set; }

    [JsonPropertyName("free_cancellation_until_time")]
    public string? FreeCancellationUntilTime { get; set; }
}
