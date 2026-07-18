using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public sealed class FlightPriceInsights
{
    [JsonPropertyName("lowest_price")]
    public decimal LowestPrice { get; set; }

    [JsonPropertyName("price_level")]
    public string? PriceLevel { get; set; }

    [JsonPropertyName("typical_price_range")]
    public List<decimal>? TypicalPriceRange { get; set; }

    [JsonPropertyName("price_history")]
    public List<List<object>>? PriceHistory { get; set; }
}
