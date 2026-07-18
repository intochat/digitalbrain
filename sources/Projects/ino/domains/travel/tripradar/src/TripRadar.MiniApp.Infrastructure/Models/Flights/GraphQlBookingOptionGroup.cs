namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record GraphQlBookingOptionGroup(
        GraphQlBookingOptionDetail? Together,
        GraphQlBookingOptionDetail? Departing,
        GraphQlBookingOptionDetail? Returning,
        bool? SeparateTickets
    );
}