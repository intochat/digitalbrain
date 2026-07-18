namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record GraphQlBookingRequest(
        string? Url,
        string? PostData
    );
}