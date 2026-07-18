namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record AirportInfo(
        List<AirportDetail>? Departure,
        List<AirportDetail>? Arrival
    );
}