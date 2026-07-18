namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record FlightSegment(
        Airport DepartureAirport,
        Airport ArrivalAirport,
        int Duration,
        string? Airplane,
        string? Airline,
        string? AirlineLogo,
        string? TravelClass,
        string? FlightNumber,
        string? Legroom,
        List<string>? Extensions
    );
}