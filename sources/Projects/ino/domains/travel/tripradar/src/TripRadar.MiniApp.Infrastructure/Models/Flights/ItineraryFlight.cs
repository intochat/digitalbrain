namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed class ItineraryFlight
{
    public required LegType LegType { get; init; }
    public required int Index { get; init; }
    public required FlightSearchParams SearchParams { get; init; }
    public FlightSearchResult? Results { get; set; }
    public FlightOption? SelectedFlight { get; set; }
    public List<FlightBookingOption>? BookingProviders { get; set; }
    public bool IsBooked { get; set; }
    public string? BookedVia { get; set; }
    public bool IsLoading { get; set; }

    public string Label => LegType switch
    {
        LegType.Outbound => "Outbound",
        LegType.Return => "Return",
        _ => $"Flight {Index + 1}"
    };

    public string LabelWithDate
    {
        get
        {
            var date = FormatShortDate(SearchParams.OutboundDate);
            return LegType switch
            {
                LegType.Outbound => $"Outbound \u00b7 {date}",
                LegType.Return => $"Return \u00b7 {date}",
                _ => $"Flight {Index + 1} \u00b7 {date}"
            };
        }
    }

    public string Icon => LegType switch
    {
        LegType.Outbound => "flight_takeoff",
        LegType.Return => "flight_land",
        _ => "flight"
    };

    public string ColorClass => LegType switch
    {
        LegType.Outbound => "text-blue-600",
        LegType.Return => "text-amber-600",
        _ => "text-slate-600"
    };

    public string BorderColorClass => LegType switch
    {
        LegType.Outbound => "border-blue-600",
        LegType.Return => "border-amber-600",
        _ => "border-slate-500"
    };

    public string BgColorClass => LegType switch
    {
        LegType.Outbound => "bg-blue-50 dark:bg-blue-950/30",
        LegType.Return => "bg-amber-50 dark:bg-amber-950/30",
        _ => "bg-slate-50 dark:bg-slate-900"
    };

    public string ButtonColorClass => LegType switch
    {
        LegType.Outbound => "bg-blue-600 hover:bg-blue-700",
        LegType.Return => "bg-amber-600 hover:bg-amber-700",
        _ => "bg-slate-600 hover:bg-slate-700"
    };

    public string GetFlightSummary(string currency)
    {
        if (SelectedFlight is not { } f) return "";
        var airline = f.Flights.FirstOrDefault()?.Airline ?? "";
        var time = f.Flights.FirstOrDefault()?.DepartureAirport.Time;
        if (time?.Contains(' ') == true)
            time = time.Split(' ')[1];
        return $"{airline} \u00b7 {time} \u00b7 {Common.CurrencyFormat.FormatPrice(f.Price, currency)}";
    }

    public string RouteLabel
    {
        get
        {
            var dep = CityOnly(SearchParams.DepartureName);
            var arr = CityOnly(SearchParams.ArrivalName);
            var date = FormatShortDate(SearchParams.OutboundDate);
            return $"{date} \u00b7 {dep} \u2192 {arr}";
        }
    }

    private static string CityOnly(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var idx = name.IndexOf(',');
        return idx > 0 ? name[..idx] : name;
    }

    private static string FormatShortDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr) || !DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", out var d))
            return "";
        return d.ToString("ddd, MMM d");
    }
}