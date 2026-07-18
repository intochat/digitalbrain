using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

namespace TripRadar.MiniApp.Pages.Hotels;

public partial class HotelDetails
{
    [Parameter] public string Token { get; set; } = "";

    private HotelProperty? _hotel;
    private bool _notFound;
    private bool _isLoading;
    private string? _error;
    private string? _activeTab;
    private string[] Tabs => [L["HotelDetailsOverview"], L["HotelDetailsPrices"], L["HotelDetailsPhotos"]];

    protected override async Task OnInitializedAsync()
    {
        _activeTab ??= L["HotelDetailsOverview"];

        await UserPrefs.LoadAsync();

        var decodedToken = Uri.UnescapeDataString(Token);
        _hotel = SearchState.Results?.Properties?
            .FirstOrDefault(p => p.PropertyToken == decodedToken);

        if (_hotel is not null) return;

        _isLoading = true;
        try
        {
            _hotel = await HotelApi.GetPropertyDetailsAsync(SearchState.Params, decodedToken);
            _notFound = _hotel is null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task OpenBooking()
    {
        if (_hotel?.Link is { } link)
            await JS.InvokeVoidAsync("tg.open", link);
    }
}
