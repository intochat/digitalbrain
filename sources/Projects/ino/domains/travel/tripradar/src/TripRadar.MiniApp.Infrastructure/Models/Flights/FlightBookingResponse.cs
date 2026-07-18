namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record FlightBookingResponse(
        List<FlightOption>? Flights,
        List<FlightBookingOption>? BookingOptions
    );
}