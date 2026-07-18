using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class MultiCityForm
{
    [Parameter] public EventCallback<int> OnLegDateClick { get; set; }

    private void AddLeg() => SearchState.AddMultiCityLeg();

    private void RemoveLeg(int index) => SearchState.RemoveMultiCityLeg(index);

    private void OnDateClick(int index)
    {
        var leg = SearchState.MultiCityLegs[index];
        if (!HasLegRoute(leg)) return;
        OnLegDateClick.InvokeAsync(index);
    }

    private static bool HasLegRoute(MultiCityLeg leg) => !string.IsNullOrEmpty(leg.DepartureId) && !string.IsNullOrEmpty(leg.ArrivalId);

    private static string GetCityName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "";
        var commaIndex = displayName.IndexOf(',');
        return commaIndex > 0 ? displayName[..commaIndex] : displayName;
    }

    private static string GetFlag(string? countryCode) => CitySuggestion.CountryCodeToFlag(countryCode ?? "");

    private string GetBadge(string? airportIds)
    {
        if (string.IsNullOrEmpty(airportIds)) return "";
        var codes = airportIds.Split(',');
        return codes.Length > 1 ? string.Format(L["MultiCityAirportCount"], codes.Length) : codes[0];
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
}
