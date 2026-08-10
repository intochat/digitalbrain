using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias(AliasName)]
[Description("Generic UI vocabulary: place or update one node on whatever diagram receives it")]
public sealed record Node(
    [property: Id(0)] string NodeId,
    [property: Id(1)] string Label,
    [property: Id(2)] string? Kind = null) : Synapse
{
    public const string AliasName = "ui.node";
}
