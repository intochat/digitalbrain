using Microsoft.AspNetCore.Components;
using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Components.Flights;

public partial class TripTypeSelector
{
    [Parameter] public FlightType Value { get; set; } = FlightType.RoundTrip;
    [Parameter] public EventCallback<FlightType> ValueChanged { get; set; }

    private (FlightType Type, string Label)[] Types => [
        (FlightType.RoundTrip, L["FlightsRoundTrip"]),
        (FlightType.OneWay, L["FlightsOneWay"]),
        (FlightType.MultiCity, L["FlightsMultiCity"]),
    ];

    private async Task Select(FlightType type)
    {
        Value = type;
        await ValueChanged.InvokeAsync(type);
    }
}
