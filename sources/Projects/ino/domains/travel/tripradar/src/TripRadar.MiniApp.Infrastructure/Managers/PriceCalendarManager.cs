using System.Globalization;
using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class PriceCalendarManager(TripRadarApiClient client) : IPriceCalendarManager
{
    public async Task<PriceCalendarResult?> GetPriceCalendarAsync(string departureId, string arrivalId, int year, int month, int? tripLengthDays = null)
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var request = new
        {
            departureId,
            arrivalId,
            year,
            month,
            gl = lang == "ru" ? "ru" : "us",
            hl = lang,
            tripLengthDays
        };

        var wrapper = await client.GraphQlAsync<PriceCalendarWrapper>(
            GraphQlQueries.FlightPriceCalendar,
            new { request });

        return wrapper?.FlightPriceCalendar;
    }


    private sealed record PriceCalendarWrapper(PriceCalendarResult FlightPriceCalendar);
}