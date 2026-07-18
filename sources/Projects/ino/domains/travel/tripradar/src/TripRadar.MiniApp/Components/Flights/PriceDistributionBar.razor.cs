using Microsoft.AspNetCore.Components;

namespace TripRadar.MiniApp.Components.Flights;

public partial class PriceDistributionBar
{
    [Parameter] public decimal Low { get; set; }
    [Parameter] public decimal Typical { get; set; }
    [Parameter] public decimal High { get; set; }
    [Parameter] public string Currency { get; set; } = "USD";

    private static int Percent(decimal value, decimal total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)(value / total * 100), 0, 100);
    }
}
