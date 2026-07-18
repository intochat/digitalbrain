using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class PopularDestinations
{
    [Parameter] public string? DepartureId { get; set; }
    [Parameter] public string? DepartureName { get; set; }
    [Parameter] public string Currency { get; set; } = "USD";
    [Parameter] public EventCallback<ExploreDestination> OnDestinationClicked { get; set; }

    private List<ExploreDestination> _destinations = [];
    private bool _isLoading;
    private string? _loadedDepartureId;

    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrEmpty(DepartureId) || DepartureId == _loadedDepartureId)
            return;

        _loadedDepartureId = DepartureId;
        _isLoading = true;
        _destinations = [];
        StateHasChanged();

        try
        {
            var result = await ExploreService.GetPopularDestinationsAsync(DepartureId);
            _destinations = result?.Destinations?.Take(10).ToList() ?? [];
        }
        catch
        {
            _destinations = [];
        }
        finally
        {
            _isLoading = false;
        }
    }
}
