using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Replaces a named chart with the supplied points.</summary>
[GenerateSerializer]
[Alias("ui.render-chart")]
public sealed record RenderChart(
    CommandId Id,
    [property: Id(0)] string Title,
    [property: Id(1)] string ChartKind,
    [property: Id(2)] IReadOnlyList<ChartPoint> Points) : Command(Id);
