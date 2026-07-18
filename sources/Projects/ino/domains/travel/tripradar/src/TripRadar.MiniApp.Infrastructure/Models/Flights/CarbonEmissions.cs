namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record CarbonEmissions(
        int? ThisFlight,
        int? TypicalForThisRoute,
        int? DifferencePercent
    );
}