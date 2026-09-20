using DigitalBrain.Contracts;

namespace DigitalBrain.Flutter.Chart.Signals;

[GenerateSerializer, Alias("ui.chart-changed")]
public sealed record ChartChanged([property: Id(0)] string Name, [property: Id(1)] int Version, [property: Id(2)] string Title) : Signal;
