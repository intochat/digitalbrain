using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class PriceBarChart
{
    [Parameter] public List<FlightPriceHistoryPoint>? Points { get; set; }
    [Parameter] public FlightPriceHistoryPoint? SelectedPoint { get; set; }
    [Parameter] public EventCallback<FlightPriceHistoryPoint> OnPointClicked { get; set; }
    [Parameter] public string Currency { get; set; } = "USD";

    private static int PriceBarHeight(decimal price, decimal maxPrice)
    {
        if (maxPrice <= 0)
        {
            return 4;
        }

        return Math.Clamp((int)(price / maxPrice * 100), 4, 100);
    }

    private static string PriceBarClass(bool isSelected, bool isCheapest) =>
        isSelected ? "price-bar price-bar-selected" :
        isCheapest ? "price-bar price-bar-cheapest" :
        "price-bar price-bar-default";
}
