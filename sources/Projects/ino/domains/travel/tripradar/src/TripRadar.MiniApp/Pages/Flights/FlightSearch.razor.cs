using TripRadar.MiniApp.Client.Infrastructure.Models.Common;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Pages.Flights;

public partial class FlightSearch
{
    private bool _showPriceCalendar;
    private bool _calendarIsReturn;
    private int _multiCityDateIndex;

    private string Today => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

    private string ReturnMinDate => string.IsNullOrEmpty(SearchState.Params.OutboundDate)
        ? Today
        : SearchState.Params.OutboundDate;

    private string DateGridClass => SearchState.Params.Type == FlightType.RoundTrip
        ? "grid grid-cols-2 gap-3"
        : "grid grid-cols-1 gap-3";

    private int? CalendarTripLengthDays => SearchState.Params.Type == FlightType.RoundTrip
        ? ResolveRoundTripLengthDays()
        : null;

    private bool CanSearch => SearchState.Params.Type == FlightType.MultiCity
        ? SearchState.MultiCityLegs.Count >= 2
          && SearchState.MultiCityLegs.All(l =>
              !string.IsNullOrEmpty(l.DepartureId)
              && !string.IsNullOrEmpty(l.ArrivalId)
              && !string.IsNullOrEmpty(l.Date))
        : !string.IsNullOrEmpty(SearchState.Params.DepartureId)
          && !string.IsNullOrEmpty(SearchState.Params.ArrivalId)
          && !string.IsNullOrEmpty(SearchState.Params.OutboundDate)
          && (SearchState.Params.Type != FlightType.RoundTrip || !string.IsNullOrEmpty(SearchState.Params.ReturnDate));

    private static string AddOneDay(string dateStr)
    {
        return DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", out var d)
            ? d.AddDays(1).ToString("yyyy-MM-dd")
            : dateStr;
    }

    private string? FormatDateDisplay(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr) || !DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", out var date))
            return null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date == today) return L["FlightsToday"];
        if (date == today.AddDays(1)) return L["FlightsTomorrow"];
        return date.ToString("ddd, MMM d");
    }

    private void SwapAirports()
    {
        (SearchState.Params.DepartureId, SearchState.Params.ArrivalId) = (SearchState.Params.ArrivalId, SearchState.Params.DepartureId);
        (SearchState.Params.DepartureName, SearchState.Params.ArrivalName) = (SearchState.Params.ArrivalName, SearchState.Params.DepartureName);
        (SearchState.Params.DepartureCountryCode, SearchState.Params.ArrivalCountryCode) = (SearchState.Params.ArrivalCountryCode, SearchState.Params.DepartureCountryCode);
    }

    private static string GetCityName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "";
        var commaIndex = displayName.IndexOf(',');
        return commaIndex > 0 ? displayName[..commaIndex] : displayName;
    }

    private static string GetFlag(string? countryCode) =>
        CitySuggestion.CountryCodeToFlag(countryCode ?? "");

    private string GetBadge(string? airportIds)
    {
        if (string.IsNullOrEmpty(airportIds)) return "";
        var codes = airportIds.Split(',');
        return codes.Length > 1 ? string.Format(L["CitySearchAirports"], codes.Length) : codes[0];
    }

    private int? ResolveRoundTripLengthDays()
    {
        if (DateOnly.TryParseExact(SearchState.Params.OutboundDate, "yyyy-MM-dd", out var outbound)
            && DateOnly.TryParseExact(SearchState.Params.ReturnDate, "yyyy-MM-dd", out var inbound)
            && inbound > outbound)
        {
            return Math.Clamp(inbound.DayNumber - outbound.DayNumber, 1, 30);
        }

        return null;
    }

    private void OnPassengersChanged((int Adults, int Children, int Infants) p)
    {
        SearchState.Params.Adults = p.Adults;
        SearchState.Params.Children = p.Children;
        SearchState.Params.Infants = p.Infants;
    }

    private bool HasRoute => !string.IsNullOrEmpty(SearchState.Params.DepartureId)
                          && !string.IsNullOrEmpty(SearchState.Params.ArrivalId);

    private void OnMultiCityDateClick(int index)
    {
        _multiCityDateIndex = index;
        _calendarIsReturn = false;
        _showPriceCalendar = true;
    }

    private void OnDepartureDateClick()
    {
        if (!HasRoute) return;
        _calendarIsReturn = false;
        _showPriceCalendar = true;
    }

    private void OnReturnDateClick()
    {
        _calendarIsReturn = true;
        _showPriceCalendar = true;
    }

    private void OnPriceCalendarDateSelected(string date)
    {
        if (SearchState.Params.Type == FlightType.MultiCity)
        {
            SearchState.MultiCityLegs[_multiCityDateIndex].Date = date;
            _showPriceCalendar = false;
            return;
        }

        if (_calendarIsReturn)
        {
            SearchState.Params.ReturnDate = date;
        }
        else
        {
            SearchState.Params.OutboundDate = date;

            if (SearchState.Params.Type == FlightType.RoundTrip
                && !string.IsNullOrEmpty(SearchState.Params.ReturnDate)
                && string.Compare(SearchState.Params.ReturnDate, date, StringComparison.Ordinal) <= 0)
            {
                SearchState.Params.ReturnDate = AddOneDay(date);
            }
        }

        Prefetch.RequestPrefetch(SearchState.Params);
    }

    private void Search()
    {
        if (SearchState.Params.Type == FlightType.RoundTrip)
            SearchState.RoundTripMode = RoundTripMode.PairedDeals;

        SearchState.BuildItinerary();
        Nav.NavigateTo(AppRoutes.FlightResults);
    }
}
