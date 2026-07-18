using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Models;

internal sealed class KiwiPricePerDateItem
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("ratedPrice")]
    public KiwiRatedPrice? RatedPrice { get; set; }
}