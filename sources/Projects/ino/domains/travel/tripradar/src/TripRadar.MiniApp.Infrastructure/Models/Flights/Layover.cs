namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record Layover(
        int Duration,
        string? Name,
        string? Id
    );
}