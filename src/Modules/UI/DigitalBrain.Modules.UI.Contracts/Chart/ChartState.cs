namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chart-state")]
public sealed record ChartState(
    [property: Id(0)] string Title,
    [property: Id(1)] string ChartKind,
    [property: Id(2)] IReadOnlyList<ChartPoint> Points);

[GenerateSerializer]
[Alias("ui.chart-point")]
public sealed record ChartPoint(
    [property: Id(0)] string Label,
    [property: Id(1)] double Value);
