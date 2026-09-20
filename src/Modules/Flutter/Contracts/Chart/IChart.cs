using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Chart;

[Alias("chart"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ChartType)]
public interface IChart : INeuron
{
    Task Render(string title, string kind, IReadOnlyList<ChartPoint> points);
    Task Append(ChartPoint point);
    [ReadOnly, Alias("read")] Task<ChartState> Read();
}

[GenerateSerializer, Alias("ui.chart-point")]
public sealed record ChartPoint(
    [property: Id(0)] string EventId,
    [property: Id(1)] string Label,
    [property: Id(2)] double Value);

[GenerateSerializer, Alias("ui.chart-state")]
public sealed class ChartState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string Title { get; set; } = "";
    [Id(3)] public string Kind { get; set; } = "bar";
    [Id(4)] public List<ChartPoint> Points { get; set; } = [];
}
