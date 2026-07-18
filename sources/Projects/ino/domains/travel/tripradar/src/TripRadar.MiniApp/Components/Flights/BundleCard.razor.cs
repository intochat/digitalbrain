using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class BundleCard
{
    [Parameter, EditorRequired] public FlightBundle Bundle { get; set; } = default!;
    [Parameter] public EventCallback OnSelect { get; set; }
    [Parameter] public string Currency { get; set; } = "USD";

    private string TagLabel => Bundle.Tag switch
    {
        FlightBundleTag.Best => L["FlightsBundleBest"],
        FlightBundleTag.Cheapest => L["FlightsBundleCheapest"],
        FlightBundleTag.Fastest => L["FlightsBundleFastest"],
        _ => ""
    };

    private string TagCss => Bundle.Tag switch
    {
        FlightBundleTag.Best => "text-xs font-bold px-2.5 py-0.5 rounded-full bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400",
        FlightBundleTag.Cheapest => "text-xs font-bold px-2.5 py-0.5 rounded-full bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400",
        FlightBundleTag.Fastest => "text-xs font-bold px-2.5 py-0.5 rounded-full bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-400",
        _ => ""
    };
}
