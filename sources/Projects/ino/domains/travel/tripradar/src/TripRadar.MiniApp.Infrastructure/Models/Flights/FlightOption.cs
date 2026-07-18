namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record FlightOption(
        List<FlightSegment> Flights,
        List<Layover>? Layovers,
        int TotalDuration,
        decimal Price,
        string? Type,
        string? AirlineLogo,
        string? BookingToken,
        string? DepartureToken,
        CarbonEmissions? CarbonEmissions
    );
}