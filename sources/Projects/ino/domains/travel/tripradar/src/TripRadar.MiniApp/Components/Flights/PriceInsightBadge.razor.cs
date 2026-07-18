using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class PriceInsightBadge
{
    [Parameter] public FlightPriceInsight? Insight { get; set; }

    private (string Color, string Icon, string Label) GetLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "low" => ("text-green-600", "trending_down", L["PriceInsightLow"]),
        "typical" => ("text-blue-500", "trending_flat", L["PriceInsightTypical"]),
        "high" => ("text-orange-500", "trending_up", L["PriceInsightHigh"]),
        _ => ("text-slate-500", "info", L["PriceInsightInfo"])
    };
}
