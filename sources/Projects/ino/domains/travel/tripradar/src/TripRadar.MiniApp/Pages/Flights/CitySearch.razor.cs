using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Pages.Flights;

public partial class CitySearch
{
    [Parameter] public string Field { get; set; } = "";

    private ElementReference _inputRef;
    private string _query = "";
    private List<CitySuggestion> _suggestions = [];
    private bool _isLoading;
    private CancellationTokenSource? _debounce;

    protected override void OnInitialized()
    {
        if (SearchState.PendingSearchQuery is not null)
        {
            _query = SearchState.PendingSearchQuery;
            _suggestions = SearchState.PendingSearchResults ?? [];
            SearchState.PendingSearchQuery = null;
            SearchState.PendingSearchResults = null;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await _inputRef.FocusAsync();
        }
    }

    private async Task OnInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? "";
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        if (string.IsNullOrWhiteSpace(_query))
        {
            _suggestions.Clear();
            return;
        }

        try
        {
            _isLoading = true;
            await Task.Delay(300, token);
            if (token.IsCancellationRequested) return;

            var airports = await AirportApi.SearchAsync(_query);
            _suggestions = airports
                .GroupBy(a => (a.City, a.CountryCode))
                .Select(g =>
                {
                    var list = g.ToList();
                    if (list.Count > 1)
                    {
                        var withCoords = list.Where(a => a.Latitude.HasValue && a.Longitude.HasValue).ToList();
                        if (withCoords.Count > 0)
                        {
                            var centerLat = withCoords.Average(a => a.Latitude!.Value);
                            var centerLon = withCoords.Average(a => a.Longitude!.Value);
                            list = list.Select(a => a with
                            {
                                DistanceFromCenter = CitySuggestion.ComputeDistanceKm(a.Latitude, a.Longitude, centerLat, centerLon)
                            }).ToList();
                        }
                    }
                    return new CitySuggestion(
                        g.First().City,
                        g.First().CountryCode,
                        list);
                })
                .ToList();
            _isLoading = false;
            StateHasChanged();
        }
        catch (TaskCanceledException) { }
    }

    private string? OppositeFieldCodes
    {
        get
        {
            if (TryParseMultiCityField(out var idx, out var dir))
            {
                var leg = SearchState.MultiCityLegs.ElementAtOrDefault(idx);
                return dir == "departure" ? leg?.ArrivalId : leg?.DepartureId;
            }
            return Field == "departure" ? SearchState.Params.ArrivalId : SearchState.Params.DepartureId;
        }
    }

    private bool IsSameAsOpposite(CitySuggestion city) =>
        !string.IsNullOrEmpty(OppositeFieldCodes) &&
        string.Equals(city.Codes, OppositeFieldCodes, StringComparison.OrdinalIgnoreCase);

    private void SelectCity(CitySuggestion city)
    {
        if (IsSameAsOpposite(city)) return;

        var localizedName = CityNames.GetName(city.City);

        if (TryParseMultiCityField(out var legIndex, out var direction))
        {
            var leg = SearchState.MultiCityLegs.ElementAtOrDefault(legIndex);
            if (leg is null) { GoBack(); return; }

            if (direction == "departure")
            {
                leg.DepartureId = city.Codes;
                leg.DepartureName = localizedName;
                leg.DepartureCountryCode = city.CountryCode;
            }
            else
            {
                leg.ArrivalId = city.Codes;
                leg.ArrivalName = localizedName;
                leg.ArrivalCountryCode = city.CountryCode;

                if (legIndex + 1 < SearchState.MultiCityLegs.Count)
                {
                    var next = SearchState.MultiCityLegs[legIndex + 1];
                    if (string.IsNullOrEmpty(next.DepartureId))
                    {
                        next.DepartureId = city.Codes;
                        next.DepartureName = localizedName;
                        next.DepartureCountryCode = city.CountryCode;
                    }
                }
            }
        }
        else if (Field == "departure")
        {
            SearchState.Params.DepartureId = city.Codes;
            SearchState.Params.DepartureName = localizedName;
            SearchState.Params.DepartureCountryCode = city.CountryCode;
        }
        else
        {
            SearchState.Params.ArrivalId = city.Codes;
            SearchState.Params.ArrivalName = localizedName;
            SearchState.Params.ArrivalCountryCode = city.CountryCode;
        }
        GoBack();
    }

    private void OpenAirportDetail(CitySuggestion city)
    {
        SearchState.PendingCitySuggestion = city;
        SearchState.PendingSearchQuery = _query;
        SearchState.PendingSearchResults = _suggestions;
        Nav.NavigateTo(AppRoutes.FlightAirportDetailFor(Field));
    }

    private void GoBack() => Nav.NavigateTo(AppRoutes.Flights);

    private void ClearInput()
    {
        _query = "";
        _suggestions.Clear();
    }

    private string FormatAirportCodes(CitySuggestion city)
    {
        if (city.Airports.Count == 1)
        {
            var a = city.Airports[0];
            return $"{AirportNames.GetName(a.Code, a.Name)} ({a.Code})";
        }
        return string.Join(" · ", city.Airports.Select(a => a.Code));
    }

    private bool TryParseMultiCityField(out int index, out string direction)
    {
        index = 0;
        direction = "";
        if (Field is null || !Field.StartsWith("mc-")) return false;
        var parts = Field.Split('-');
        if (parts.Length != 3 || !int.TryParse(parts[1], out index)) return false;
        direction = parts[2];
        return direction is "departure" or "arrival";
    }

    public void Dispose()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
    }
}
