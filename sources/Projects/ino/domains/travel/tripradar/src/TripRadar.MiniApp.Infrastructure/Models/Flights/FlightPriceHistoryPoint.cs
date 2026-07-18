using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record FlightPriceHistoryPoint(
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("price")] decimal Price
);