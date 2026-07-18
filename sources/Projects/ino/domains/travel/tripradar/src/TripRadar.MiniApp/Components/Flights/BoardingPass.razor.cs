using System.Globalization;
using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class BoardingPass
{
    [Parameter, EditorRequired] public List<ItineraryFlight> Itinerary { get; set; } = [];
    [Parameter] public int CurrentIndex { get; set; }
    [Parameter] public bool ShowTotal { get; set; } = true;
    [Parameter] public string Currency { get; set; } = "USD";

    private string FormatLabelWithDate(ItineraryFlight flight)
    {
        var label = flight.LegType switch
        {
            LegType.Outbound => L["FlightsOutbound"],
            LegType.Return => L["FlightsReturn"],
            _ => string.Format(L["MultiCityFlightNumber"], flight.Index + 1)
        };

        var date = FormatShortDate(flight.SearchParams.OutboundDate);
        return string.IsNullOrEmpty(date) ? label : $"{label} \u00b7 {date}";
    }

    private static string FormatShortDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr) || !DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", out var date))
            return "";

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
            ? date.ToString("ddd, d MMM", CultureInfo.CurrentUICulture)
            : date.ToString("ddd, MMM d", CultureInfo.CurrentUICulture);
    }

    private static string ExtractTime(string? dateTime)
    {
        if (string.IsNullOrEmpty(dateTime)) return "";
        if (dateTime.Contains(' '))
        {
            var timePart = dateTime.Split(' ')[1];
            return timePart.Length >= 5 ? timePart[..5] : timePart;
        }
        return dateTime;
    }

    private static string AirportCode(Airport airport) => airport.Code ?? (airport.Name is { Length: >= 3 } name ? name[..3].ToUpper() : airport.Name?.ToUpper()) ?? "";

    private static string FormatDuration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
