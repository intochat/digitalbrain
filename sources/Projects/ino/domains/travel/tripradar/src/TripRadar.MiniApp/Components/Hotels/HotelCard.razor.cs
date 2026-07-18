using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

namespace TripRadar.MiniApp.Components.Hotels;

public partial class HotelCard
{
    [Parameter, EditorRequired] public HotelProperty Hotel { get; set; } = default!;
    [Parameter] public string Currency { get; set; } = "USD";

    private void ViewDetails()
    {
        if (Hotel.PropertyToken is { } token)
            Nav.NavigateTo(AppRoutes.HotelDetailsFor(token));
    }
}
