using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-opened")]
[Description("Content was opened on a UI surface")]
public sealed record SurfaceOpened(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Surface,
    [property: Id(2)] string SurfaceKey,
    [property: Id(3)] string Title) : Synapse;
