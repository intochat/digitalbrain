namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record Airport(
        string Name,
        string? Code,
        string? Time
    );
}