namespace TripRadar.MiniApp.Components.Flights;

public partial class ContinueBookingBar
{
    private bool Visible => SearchState.HasPendingBooking && !IsOnBookingPage;

    private bool IsOnBookingPage => Nav.Uri.Contains("/flights/booking", StringComparison.OrdinalIgnoreCase);

    private string Summary
    {
        get
        {
            var dep = CityOnly(SearchState.Params.DepartureName);
            var arr = CityOnly(SearchState.Params.ArrivalName);
            var price = SearchState.TotalPrice;
            var airlines = string.Join(" + ", SearchState.Itinerary
                .Where(f => f.SelectedFlight?.Flights.Count > 0)
                .Select(f => f.SelectedFlight!.Flights[0].Airline)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct());
            return $"{dep} \u21c4 {arr} \u00b7 {airlines} \u00b7 ${price}";
        }
    }

    private void NavigateToBooking() => Nav.NavigateTo(AppRoutes.FlightBookingFor("trip"));

    private static string CityOnly(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var idx = name.IndexOf(',');
        return idx > 0 ? name[..idx] : name;
    }

    protected override void OnInitialized()
    {
        SearchState.OnChanged += OnStateChanged;
        Nav.LocationChanged += OnLocationChanged;
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        SearchState.OnChanged -= OnStateChanged;
        Nav.LocationChanged -= OnLocationChanged;
    }
}
