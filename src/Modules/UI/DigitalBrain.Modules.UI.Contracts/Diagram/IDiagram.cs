using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[ClientEntryPoint]
[Alias("ui.diagram")]
public partial interface IDiagram : INeuron, IHandle<Node>, IHandle<Edge>
{
    [Alias(nameof(Read))]
    Task<DiagramRead> Read();
}