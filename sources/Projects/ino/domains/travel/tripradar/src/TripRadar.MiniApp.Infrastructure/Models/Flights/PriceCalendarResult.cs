using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record PriceCalendarResult(
    [property: JsonPropertyName("days")] List<PriceCalendarDay> Days,
    [property: JsonPropertyName("cheapestDate")] string? CheapestDate,
    [property: JsonPropertyName("cheapestPrice")] decimal? CheapestPrice
);