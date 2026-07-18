namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record GraphQlBookingOptionDetail(
        string? BookWith,
        decimal? Price,
        string? AirlineLogo,
        bool? Airline,
        List<string>? MarketedAs,
        List<string>? BaggagePrices,
        GraphQlBookingRequest? BookingRequest
    );
}