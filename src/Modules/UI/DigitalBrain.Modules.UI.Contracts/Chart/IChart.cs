using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.chart")]
public interface IChart : INeuron
{
    /// <summary>Renders a chart and returns its instance name.</summary>
    [Alias("render")]
    Task<Accepted<string>> Render(RenderChart command, CancellationToken cancellationToken = default);

    /// <summary>Appends a point and returns the chart instance name.</summary>
    [Alias("append")]
    Task<Accepted<string>> Append(AppendChartPoint command, CancellationToken cancellationToken = default);

    /// <summary>Reads the chart snapshot.</summary>
    [ReadOnly, Alias("read")]
    Task<ChartState> Read();
}
