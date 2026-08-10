using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.diagram-read")]
public sealed record DiagramRead(
    [property: Id(0)] IReadOnlyList<Node> Nodes,
    [property: Id(1)] IReadOnlyList<Edge> Edges);

[ClientEntryPoint]
[Alias("ui.diagram")]
[Description("Diagram control with identity; renders whatever nodes and edges are routed at it")]
public partial interface IDiagram : INeuron, IHandle<Node>, IHandle<Edge>
{
    [Alias(nameof(Read))]
    Task<DiagramRead> Read();
}
