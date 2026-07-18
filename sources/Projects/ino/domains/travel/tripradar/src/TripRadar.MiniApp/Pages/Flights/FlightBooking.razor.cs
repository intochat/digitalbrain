using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Pages.Flights;

public partial class FlightBooking
{
    [Parameter] public string Token { get; set; } = "";

    private List<FlightBookingOption>? _currentProviders;
    private bool _loading = true;
    private string? _error;
    private string _title = "";
    private string _subtitle = "";
    private int _bookingFlightIndex;
    private string? _pendingBookingProvider;
    private DotNetObjectReference<FlightBooking>? _jsRef;

    protected override async Task OnInitializedAsync()
    {
        await UserPrefs.LoadAsync();
        BuildTitle();
        BuildSubtitle();
        _bookingFlightIndex = SearchState.NextUnbookedIndex ?? 0;
        await LoadCurrentProviders();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && SearchState.IsMultiLeg)
        {
            _jsRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("tripRadar.registerVisibilityCallback", _jsRef);
        }
    }

    [JSInvokable]
    public void OnPageVisible()
    {
        if (_pendingBookingProvider is null) return;

        var flightToMark = SearchState.Itinerary.ElementAtOrDefault(_bookingFlightIndex);
        if (flightToMark is not null)
        {
            flightToMark.IsBooked = true;
            flightToMark.BookedVia = _pendingBookingProvider;
        }
        _pendingBookingProvider = null;

        var nextIndex = SearchState.NextUnbookedIndex;
        if (nextIndex.HasValue)
        {
            _bookingFlightIndex = nextIndex.Value;
            _ = LoadCurrentProviders();
        }

        SearchState.NotifyChanged();
        StateHasChanged();
    }

    private async Task OnBookClick(FlightBookingOption option)
    {
        if (SearchState.IsMultiLeg)
        {
            _pendingBookingProvider = option.BookWith;
            return;
        }

        if (!string.IsNullOrEmpty(option.PostData) && !string.IsNullOrEmpty(option.Url))
            await JS.InvokeVoidAsync("tg.postOpen", option.Url, option.PostData);
        else if (!string.IsNullOrEmpty(option.Url))
            await JS.InvokeVoidAsync("tg.open", option.Url);
    }

    private async Task LoadCurrentProviders()
    {
        _loading = true;
        _error = null;
        _currentProviders = null;
        StateHasChanged();

        try
        {
            var flight = SearchState.Itinerary.ElementAtOrDefault(_bookingFlightIndex);
            var token = flight?.SelectedFlight?.BookingToken;

            // In paired/bundle round-trip mode, outbound has DepartureToken, not BookingToken.
            // The return flight's BookingToken covers the full round-trip booking.
            if (string.IsNullOrEmpty(token) && SearchState.UsesRoundTripToken)
            {
                var returnFlight = SearchState.Itinerary.FirstOrDefault(f => f.SelectedFlight?.BookingToken is not null);
                token = returnFlight?.SelectedFlight?.BookingToken;
                flight = returnFlight ?? flight;
            }

            if (string.IsNullOrEmpty(token) || flight is null)
            {
                _error = L["BookingNoFlightSelected"];
                return;
            }

            var booking = await FlightApi.GetBookingAsync(token, flight.SearchParams);
            _currentProviders = booking?.BookingOptions;
        }
        catch (Exception ex)
        {
            _error = GetBookingErrorMessage(ex);
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private string GetBookingErrorMessage(Exception exception)
    {
        if (exception is OperationCanceledException || IsTimeoutMessage(exception.Message))
        {
            return L["BookingTimeout"];
        }

        return string.IsNullOrWhiteSpace(exception.Message) ? L["CommonError"] : exception.Message;
    }

    private static bool IsTimeoutMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message)
        && message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

    private void BuildTitle()
    {
        var p = SearchState.Params;
        if (!string.IsNullOrEmpty(p.DepartureName) && !string.IsNullOrEmpty(p.ArrivalName))
        {
            var from = CityOnly(p.DepartureName);
            var to = CityOnly(p.ArrivalName);
            _title = p.Type == FlightType.RoundTrip ? $"{from} \u21c4 {to}" : $"{from} \u2192 {to}";
        }
        else
            _title = L["BookingTitle"];
    }

    private void BuildSubtitle()
    {
        var parts = new List<string>();
        var p = SearchState.Params;

        parts.Add(p.Type switch
        {
            FlightType.OneWay => L["BookingOneWay"],
            FlightType.RoundTrip => L["BookingRoundTrip"],
            _ => L["BookingMultiCity"]
        });

        if (!string.IsNullOrEmpty(p.OutboundDate) && DateTime.TryParse(p.OutboundDate, out var date))
        {
            if (p.Type == FlightType.RoundTrip && !string.IsNullOrEmpty(p.ReturnDate) && DateTime.TryParse(p.ReturnDate, out var retDate))
                parts.Add($"{date:MMM d}\u2013{retDate:MMM d}");
            else
                parts.Add(date.ToString("MMM d"));
        }

        var total = p.TotalPassengers;
        parts.Add(string.Format(total == 1 ? L["BookingPassenger"] : L["BookingPassengers"], total));

        _subtitle = string.Join(" \u00b7 ", parts);
    }

    private string LocalizedLegLabel(ItineraryFlight flight) => flight.LegType switch
    {
        LegType.Outbound => L["FlightsOutbound"],
        LegType.Return => L["FlightsReturn"],
        _ => string.Format(L["MultiCityFlightNumber"], flight.Index + 1)
    };

    private static string CityOnly(string name)
    {
        var commaIndex = name.IndexOf(',');
        return commaIndex > 0 ? name[..commaIndex] : name;
    }

    public void Dispose()
    {
        if (_jsRef is not null)
        {
            _ = JS.InvokeVoidAsync("tripRadar.unregisterVisibilityCallback");
            _jsRef.Dispose();
        }
    }
}
