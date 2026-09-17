using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.chart")]
public interface IChart : INeuron
{
    /// <summary>Renders a chart and returns its instance name.</summary>
    [Alias("render")]
    [NeuronTool]
    Task<Accepted<string>> Render(RenderChart command, CancellationToken cancellationToken = default);

    /// <summary>Appends a point and returns the chart instance name.</summary>
    [Alias("append")]
    [NeuronTool]
    Task<Accepted<string>> Append(AppendChartPoint command, CancellationToken cancellationToken = default);

    /// <summary>Reads the chart snapshot.</summary>
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ChartState> Read();
}
