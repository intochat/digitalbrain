using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias(AliasName)]
[Description("Generic UI vocabulary: draw or update one directed edge on whatever diagram receives it")]
public sealed record Edge(
    [property: Id(0)] string EdgeId,
    [property: Id(1)] string SourceNodeId,
    [property: Id(2)] string TargetNodeId,
    [property: Id(3)] string? Label = null,
    [property: Id(4)] string? Kind = null) : Synapse
{
    public const string AliasName = "ui.edge";
}
