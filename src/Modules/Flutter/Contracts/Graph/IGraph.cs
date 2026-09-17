using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.graph")]
public interface IGraph : INeuron
{
    /// <summary>Renders a graph and returns its instance name.</summary>
    [Alias("render")]
    [NeuronTool]
    Task<Accepted<string>> Render(RenderGraph command, CancellationToken cancellationToken = default);

    /// <summary>Reads the graph snapshot.</summary>
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<GraphState> Read();
}
