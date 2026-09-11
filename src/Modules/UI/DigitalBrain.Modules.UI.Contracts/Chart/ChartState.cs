namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chart-state")]
public sealed record ChartState(
    [property: Id(0)] string Title,
    [property: Id(1)] string ChartKind,
    [property: Id(2)] IReadOnlyList<ChartPoint> Points)
{
    public const int MaxPoints = 512;
}
