using System.Globalization;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class FlightExploreManager(TripRadarApiClient client) : IFlightExploreManager
{
    public async Task<FlightExploreResult?> GetPopularDestinationsAsync(string departureId)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var request = new
        {
            departureId,
            gl = lang == "ru" ? "ru" : "us",
            hl = lang,
            currency = lang == "ru" ? "RUB" : "USD"
        };

        var wrapper = await client.GraphQlAsync<ExploreWrapper>(
            GraphQlQueries.FlightExplore,
            new { request });

        return wrapper?.FlightExplore;
    }

    private sealed record ExploreWrapper(FlightExploreResult FlightExplore);
}