namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record FlightSearchResult(
    List<FlightOption>? BestFlights,
    List<FlightOption>? OtherFlights,
    FlightPriceInsight? PriceInsights,
    List<AirportInfo>? Airports
);