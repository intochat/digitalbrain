using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;
using TripRadar.MiniApp.Client.Infrastructure.Services.State;

namespace TripRadar.MiniApp.Pages.Flights;

public partial class FlightResults
{
    private readonly FlightFilterState _filters = new();
    private string? _activeSheet;
    private bool _trackingInProgress;
    private string? _trackingErrorTitle;
    private string? _trackingError;
    private bool _isTracking;
    private Guid? _trackingId;

    [SupplyParameterFromQuery(Name = "requestId")]
    public Guid? RequestId { get; set; }

    private IReadOnlyList<(RoundTripMode Mode, string Icon, string Label)> RoundTripModes =>
    [
        (RoundTripMode.PairedDeals, "link", L["FlightsRoundTripDeals"]),
        (RoundTripMode.Bundles, "package_2", L["FlightsBundles"]),
        (RoundTripMode.PickSeparately, "swap_horiz", L["FlightsPickSeparately"])
    ];

    private string HeaderTitle => $"{CityOnly(SearchState.Params.DepartureName)} → {CityOnly(SearchState.Params.ArrivalName)}";

    private ItineraryFlight? CurrentFlight => SearchState.CurrentItineraryFlight;
    private FlightSearchResult? ActiveResults => CurrentFlight?.Results;
    private bool ActiveLoading => CurrentFlight?.IsLoading ?? SearchState.IsLoading;

    private bool ShowSortTabs => SearchState.IsPairedMode && SearchState.CurrentFlightIndex == 0;

    private List<FlightBundle> SortedBundles =>
        (SearchState.Bundles ?? []).SortBundles(SearchState.PairedSort);

    private List<FlightOption> ActiveAllFlights =>
        (ActiveResults?.BestFlights ?? []).Concat(ActiveResults?.OtherFlights ?? []).ToList();

    private List<FlightOption> ActiveFilteredBest =>
        (ActiveResults?.BestFlights ?? Enumerable.Empty<FlightOption>()).ApplyFilters(_filters);

    private List<FlightOption> ActiveFilteredOther =>
        (ActiveResults?.OtherFlights ?? Enumerable.Empty<FlightOption>()).ApplyFilters(_filters);

    private List<FlightOption> SortedFilteredBest => ShowSortTabs
        ? ActiveAllFlights.ApplyFilters(_filters).SortPaired(SearchState.PairedSort)
        : ActiveFilteredBest;

    private List<FlightOption> SortedFilteredOther => ShowSortTabs
        ? []
        : ActiveFilteredOther;

    private HashSet<string> AvailableAirlines => ActiveAllFlights.ExtractAirlines();
    private (decimal Min, decimal Max) PriceRange => ActiveAllFlights.GetPriceRange();

    private string LocalizedLegLabel(ItineraryFlight flight) => flight.LegType switch
    {
        LegType.Outbound => L["FlightsOutbound"],
        LegType.Return => L["FlightsReturn"],
        _ => string.Format(L["MultiCityFlightNumber"], flight.Index + 1)
    };

    private string LocalizedCurrentLegLabelLower() => CurrentFlight is null
        ? string.Empty
        : LocalizedLegLabel(CurrentFlight).ToLower(System.Globalization.CultureInfo.CurrentUICulture);

    private string PairedSortLabel(PairedSortOrder sort) => sort switch
    {
        PairedSortOrder.Cheapest => L["FlightsBundleCheapest"],
        PairedSortOrder.Fastest => L["FlightsBundleFastest"],
        _ => L["FlightsBundleBest"]
    };
    private static string CityOnly(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "";
        var commaIndex = displayName.IndexOf(',');
        return commaIndex > 0 ? displayName[..commaIndex] : displayName;
    }

    protected override async Task OnInitializedAsync()
    {
        SearchState.OnChanged += StateHasChanged;
        Tracking.OnChanged += OnTrackingChanged;
        await UserPrefs.LoadAsync();
        await Tracking.LoadAsync();
        HydrateFromRequestIdIfNeeded();
        await LoadResults();
        SyncTrackingState();
    }

    private void HydrateFromRequestIdIfNeeded()
    {
        if (RequestId is null || RequestId == Guid.Empty) return;

        var tracking = Tracking.Trackings.FirstOrDefault(t => t.UniqueId == RequestId.Value);
        if (tracking is null || tracking.ServiceType != "Flights") return;
        if (string.IsNullOrEmpty(tracking.DepartureAirportCode) || string.IsNullOrEmpty(tracking.DestinationAirportCode)) return;
        if (tracking.DepartureDate is null) return;

        SearchState.Params = new FlightSearchParams
        {
            DepartureId = tracking.DepartureAirportCode,
            ArrivalId = tracking.DestinationAirportCode,
            DepartureName = tracking.DepartureAirportCity ?? tracking.DepartureAirportCode,
            ArrivalName = tracking.DestinationAirportCity ?? tracking.DestinationAirportCode,
            OutboundDate = tracking.DepartureDate.Value.ToString("yyyy-MM-dd"),
            ReturnDate = tracking.ReturnDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            Type = tracking.ReturnDate.HasValue ? FlightType.RoundTrip : FlightType.OneWay
        };
        SearchState.BuildItinerary();
    }

    private void OnTrackingChanged()
    {
        SyncTrackingState();
        StateHasChanged();
    }

    private void SyncTrackingState()
    {
        var depCode = SearchState.Params.DepartureId?.Split(',')[0];
        var arrCode = SearchState.Params.ArrivalId?.Split(',')[0];
        var match = Tracking.FindTracking(depCode, arrCode, SearchState.Params.OutboundDate);
        _isTracking = match is { IsActive: true };
        _trackingId = match?.UniqueId;
    }

    private async Task LoadResults()
    {
        if (CurrentFlight is null) return;

        var fingerprint = FlightPrefetchService.ComputeFingerprint(SearchState.Params);
        if (SearchState.CurrentFlightIndex == 0 && SearchState.TryConsumePrefetch(fingerprint, out var prefetched))
        {
            SearchState.SetCurrentResults(prefetched);
            return;
        }

        await LoadCurrentFlightResults();
    }

    private async Task LoadCurrentFlightResults()
    {
        if (CurrentFlight is null) return;
        SearchState.SetCurrentLoading();
        try
        {
            if (SearchState.IsPairedMode)
            {
                // Paired mode always uses round-trip params (type=1)
                // Step 2: pass departure_token from selected outbound to get paired return flights
                string? departureToken = null;
                if (SearchState.CurrentFlightIndex > 0)
                {
                    var outbound = SearchState.Itinerary[0].SelectedFlight;
                    departureToken = outbound?.DepartureToken;
                }

                var result = await FlightApi.SearchAsync(SearchState.Params, departureToken);
                if (result is not null)
                    SearchState.SetCurrentResults(result);
                else
                    SearchState.SetCurrentError("No flights found");
            }
            else
            {
                var result = await FlightApi.SearchAsync(CurrentFlight.SearchParams);
                if (result is not null)
                    SearchState.SetCurrentResults(result);
                else
                    SearchState.SetCurrentError("No flights found");
            }
        }
        catch (Exception ex)
        {
            SearchState.SetCurrentError(ex.Message);
        }
    }

    private bool IsFlightSelected(FlightOption flight) =>
        ReferenceEquals(flight, CurrentFlight?.SelectedFlight);

    private decimal? GetRunningTotal(FlightOption flight)
    {
        if (!SearchState.IsMultiLeg) return null;
        var previousTotal = SearchState.Itinerary
            .Where(f => f.Index < SearchState.CurrentFlightIndex && f.SelectedFlight is not null)
            .Sum(f => f.SelectedFlight!.Price);
        return previousTotal + flight.Price;
    }

    private void OnFlightSelected(FlightOption flight)
    {
        SearchState.SelectFlight(flight);
        if (!SearchState.IsMultiLeg)
            Nav.NavigateTo(AppRoutes.FlightBookingFor("trip"));
    }

    private async Task OnChooseNext()
    {
        _filters.Reset();
        SearchState.NextFlight();
        await LoadCurrentFlightResults();
    }

    private void OnTabClick(int index)
    {
        if (index < SearchState.CurrentFlightIndex)
        {
            SearchState.GoToFlight(index);
            _filters.Reset();
        }
    }

    private void OnViewBooking() =>
        Nav.NavigateTo(AppRoutes.FlightBookingFor("trip"));

    private void SetPairedSort(PairedSortOrder sort)
    {
        SearchState.PairedSort = sort;
        SearchState.NotifyChanged();
    }

    private async Task SwitchMode(RoundTripMode mode)
    {
        if (SearchState.RoundTripMode == mode) return;
        SearchState.RoundTripMode = mode;
        _filters.Reset();

        if (mode == RoundTripMode.Bundles)
        {
            BundlePrefetch.Cancel();
            var outboundResults = SearchState.Itinerary.ElementAtOrDefault(0)?.Results;
            if (outboundResults is not null)
            {
                await BundlePrefetch.LoadBundlesAsync(outboundResults);
            }
            else
            {
                // Outbound not loaded yet — load it first, then build bundles
                if (CurrentFlight is not null)
                {
                    CurrentFlight.Results = null;
                    CurrentFlight.SelectedFlight = null;
                }
                await LoadCurrentFlightResults();
                outboundResults = SearchState.Itinerary.ElementAtOrDefault(0)?.Results;
                if (outboundResults is not null)
                    await BundlePrefetch.LoadBundlesAsync(outboundResults);
            }
            return;
        }

        BundlePrefetch.Cancel();

        // Clear current results so they reload with the right API type
        if (CurrentFlight is not null)
        {
            CurrentFlight.Results = null;
            CurrentFlight.SelectedFlight = null;
        }

        await LoadCurrentFlightResults();
    }

    private void OnBundleSelected(FlightBundle bundle)
    {
        if (SearchState.Itinerary.Count < 2) return;

        SearchState.Itinerary[0].SelectedFlight = bundle.Outbound;
        SearchState.Itinerary[1].SelectedFlight = bundle.Return;
        SearchState.NotifyChanged();
        Nav.NavigateTo(AppRoutes.FlightBookingFor("trip"));
    }

    private async Task ToggleTracking()
    {
        _trackingInProgress = true;
        _trackingErrorTitle = null;
        _trackingError = null;
        StateHasChanged();

        try
        {
            var existingId = _trackingId;
            if (!existingId.HasValue)
            {
                var depCode = SearchState.Params.DepartureId?.Split(',')[0];
                var arrCode = SearchState.Params.ArrivalId?.Split(',')[0];
                existingId = Tracking.FindTracking(depCode, arrCode, SearchState.Params.OutboundDate)?.UniqueId;
            }

            if (existingId.HasValue)
            {
                await Tracking.ToggleAsync(existingId.Value);
                SyncTrackingState();
            }
            else
            {
                var depCode = SearchState.Params.DepartureId?.Split(',')[0] ?? "";
                var arrCode = SearchState.Params.ArrivalId?.Split(',')[0] ?? "";

                await Tracking.CreateFlightAsync(
                    depCode,
                    arrCode,
                    DateTime.Parse(SearchState.Params.OutboundDate, System.Globalization.CultureInfo.InvariantCulture),
                    string.IsNullOrEmpty(SearchState.Params.ReturnDate)
                        ? null
                        : DateTime.Parse(SearchState.Params.ReturnDate, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch (Exception ex)
        {
            (_trackingErrorTitle, _trackingError) = GetTrackingError(ex);
        }
        finally
        {
            _trackingInProgress = false;
        }
    }

    private (string Title, string Message) GetTrackingError(Exception exception)
    {
        if (IsSubscriptionLimitError(exception.Message))
        {
            return (L["TrackingUpgradeRequiredTitle"], L["TrackingUpgradeRequiredMessage"]);
        }

        return (L["TrackingUpdateFailedTitle"], L["TrackingUpdateFailedMessage"]);
    }

    private static bool IsSubscriptionLimitError(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && (message.Contains("Essential and Advanced", StringComparison.OrdinalIgnoreCase)
            || message.Contains("upgrade your subscription", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Scheduled queries", StringComparison.OrdinalIgnoreCase));

    private void OpenSheet(string? sheet) => _activeSheet = sheet;
    private void CloseSheet() => _activeSheet = null;

    private void ToggleStop(int stops)
    {
        if (!_filters.Stops.Remove(stops))
            _filters.Stops.Add(stops);
    }

    private void ToggleAirline(string airline)
    {
        if (!_filters.Airlines.Remove(airline))
            _filters.Airlines.Add(airline);
    }

    private void OnPriceChanged(ChangeEventArgs e)
    {
        if (decimal.TryParse(e.Value?.ToString(), out var price))
            _filters.MaxPrice = price;
    }

    private void TogglePriceFilter()
    {
        if (_filters.MaxPrice.HasValue)
        {
            _filters.MaxPrice = null;
        }
        else
        {
            var (_, maxPrice) = PriceRange;
            _filters.MaxPrice = maxPrice;
        }
    }

    private void SetTimeRange(DepartureTimeRange range) =>
        _filters.TimeRange = _filters.TimeRange == range ? null : range;

    private void ClearFilters()
    {
        _filters.Reset();
        _activeSheet = null;
    }

    private string FormatTimeRange(DepartureTimeRange range) => range switch
    {
        DepartureTimeRange.Morning => L["FilterMorning"],
        DepartureTimeRange.Afternoon => L["FilterAfternoon"],
        DepartureTimeRange.Evening => L["FilterEvening"],
        DepartureTimeRange.Night => L["FilterNight"],
        _ => range.ToString()
    };

    public void Dispose()
    {
        SearchState.OnChanged -= StateHasChanged;
        Tracking.OnChanged -= OnTrackingChanged;
        BundlePrefetch.Cancel();
    }
}
