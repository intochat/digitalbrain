using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Shared;

public partial class AirportAutocomplete
{
    [Parameter] public string Placeholder { get; set; } = "City";
    [Parameter] public string Icon { get; set; } = "flight_takeoff";
    [Parameter] public string DisplayText { get; set; } = "";
    [Parameter] public string Flag { get; set; } = "";
    [Parameter] public string Badge { get; set; } = "";
    [Parameter] public string CityName { get; set; } = "";
    [Parameter] public string Field { get; set; } = "departure";

    private void OpenSearch() => Nav.NavigateTo(AppRoutes.FlightCitySearchFor(Field));
}
