namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chart-state")]
public sealed record ChartState(
    [property: Id(0)] IReadOnlyList<ChartStatePoint> Points);

[GenerateSerializer]
[Alias("ui.chart-state-point")]
public sealed record ChartStatePoint(
    [property: Id(0)] string Series,
    [property: Id(1)] string Label,
    [property: Id(2)] double Value);
