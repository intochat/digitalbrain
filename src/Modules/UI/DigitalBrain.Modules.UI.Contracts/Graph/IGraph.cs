using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.graph")]
public interface IGraph : INeuron
{
    /// <summary>Renders a graph and returns its instance name.</summary>
    [Alias("render")]
    Task<Accepted<string>> Render(RenderGraph command, CancellationToken cancellationToken = default);

    /// <summary>Reads the graph snapshot.</summary>
    [ReadOnly, Alias("read")]
    Task<GraphState> Read();
}
