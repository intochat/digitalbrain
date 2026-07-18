using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class FlightCard
{
    [Parameter, EditorRequired] public FlightOption Flight { get; set; } = null!;
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public bool IsSelected { get; set; }
    [Parameter] public decimal? RunningTotal { get; set; }
    [Parameter] public string Currency { get; set; } = "USD";
    [Parameter] public EventCallback OnSelect { get; set; }

    private string CardCss
    {
        get
        {
            var baseCss = "bg-white dark:bg-slate-900 rounded-xl p-4 space-y-3";
            if (ReadOnly)
                return $"{baseCss} border border-slate-200 dark:border-slate-800";
            if (IsSelected)
                return $"{baseCss} border-2 border-primary ring-1 ring-primary/20 shadow-md cursor-pointer";
            return $"{baseCss} border border-slate-200 dark:border-slate-800 hover:shadow-md transition-shadow cursor-pointer";
        }
    }

    private string AirlineName => Flight.Flights.FirstOrDefault()?.Airline ?? L["FlightsUnknownAirline"];

    private string StopsLabel
    {
        get
        {
            var stops = Flight.Flights.Count - 1;
            return stops == 0 ? L["FlightsNonstop"] : string.Format(stops > 1 ? L["FlightsStopsCount"] : L["FlightsStopCount"], stops);
        }
    }

    private async Task ViewDetails()
    {
        if (ReadOnly) return;

        if (OnSelect.HasDelegate)
        {
            await OnSelect.InvokeAsync();
            return;
        }

        if (Flight.BookingToken is { } token)
            Nav.NavigateTo(AppRoutes.FlightBookingFor(token));
    }

    private string LayoverName(Layover layover, Airport arrivalAirport)
    {
        var name = StripAirportSuffix(layover.Name ?? arrivalAirport.Name);
        var isoCode = AirportCountryLookup.GetCountry(layover.Id ?? arrivalAirport.Code);
        var country = isoCode is not null ? Countries.GetName(isoCode) : null;
        return country is not null ? $"{name}, {country}" : name;
    }

    private static string StripAirportSuffix(string name)
    {
        ReadOnlySpan<string> suffixes = ["International Airport", " Airport", "-El Prat Airport"];
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                return name[..^suffix.Length].TrimEnd();
        }
        return name;
    }

    private static string FormatDuration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
