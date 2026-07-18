using System.Globalization;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class AirportManager(TripRadarApiClient client) : IAirportManager
{
    public async Task<List<AirportSuggestion>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return [];

        var encoded = Uri.EscapeDataString(query);
        var hl = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var result = await client.GetAsync<AirportSuggestionsResponse>(ApiEndpoints.AirportsSearchFor(encoded, hl));
        return result?.Airports ?? [];
    }
}