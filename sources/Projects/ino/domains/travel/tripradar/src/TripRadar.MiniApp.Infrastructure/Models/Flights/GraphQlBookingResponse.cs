namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record GraphQlBookingResponse(
        List<FlightOption>? BestFlights,
        List<GraphQlBookingOptionGroup>? BookingOptions
    );
}