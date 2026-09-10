using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Appends a chart point once per event id.</summary>
[GenerateSerializer]
[Alias("ui.append-chart-point")]
public sealed record AppendChartPoint(
    CommandId Id,
    [property: Id(0)] ChartPoint Point,
    [property: Id(1)] string Title) : Command(Id);
