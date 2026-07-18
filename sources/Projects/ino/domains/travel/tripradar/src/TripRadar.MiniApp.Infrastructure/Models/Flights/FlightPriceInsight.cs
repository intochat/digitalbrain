namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record FlightPriceInsight(
    decimal? LowestPrice,
    string? PriceLevel,
    decimal[]? TypicalPriceRange,
    List<FlightPriceHistoryPoint>? PriceHistory
);