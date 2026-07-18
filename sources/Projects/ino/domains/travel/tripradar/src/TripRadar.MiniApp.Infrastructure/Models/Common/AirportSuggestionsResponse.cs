namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record AirportSuggestionsResponse(
        List<AirportSuggestion> Airports
    );
}