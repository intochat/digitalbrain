namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record FlightBookingOption(
    string? BookWith,
    decimal? Price,
    string? AirlineLogo,
    string? Url,
    string? PostData,
    bool IsAirline,
    List<string>? MarketedAs,
    List<string>? BaggagePrices,
    bool SeparateTickets
);

// Intermediate types matching the nested GraphQL response structure