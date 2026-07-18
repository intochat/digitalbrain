using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Pages.Flights;

public partial class AirportDetail
{
    [Parameter] public string Field { get; set; } = "";

    private CitySuggestion? _city;
    private HashSet<string> _selected = [];

    protected override void OnInitialized()
    {
        _city = SearchState.PendingCitySuggestion;
        if (_city is null)
        {
            Nav.NavigateTo(AppRoutes.Flights);
            return;
        }
        _selected = _city.Airports.Select(a => a.Code).ToHashSet();
    }

    private void ToggleAirport(string code)
    {
        if (!_selected.Remove(code))
            _selected.Add(code);
    }

    private void Confirm()
    {
        if (_city is null || _selected.Count == 0) return;

        var selectedAirports = _city.Airports.Where(a => _selected.Contains(a.Code)).ToList();
        var codes = string.Join(",", selectedAirports.Select(a => a.Code));
        var display = CityNames.GetName(_city.City);

        if (Field == "departure")
        {
            SearchState.Params.DepartureId = codes;
            SearchState.Params.DepartureName = display;
            SearchState.Params.DepartureCountryCode = _city.CountryCode;
        }
        else
        {
            SearchState.Params.ArrivalId = codes;
            SearchState.Params.ArrivalName = display;
            SearchState.Params.ArrivalCountryCode = _city.CountryCode;
        }

        SearchState.PendingCitySuggestion = null;
        Nav.NavigateTo(AppRoutes.Flights);
    }

    private void GoBack()
    {
        Nav.NavigateTo(AppRoutes.FlightCitySearchFor(Field));
    }
}
