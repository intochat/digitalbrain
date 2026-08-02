using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Shell;

[GenerateSerializer]
[Alias("flutter.scene-opened")]
[Description("A shell scene was opened")]
public sealed record SceneOpened(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Shell,
    [property: Id(2)] string SceneKey,
    [property: Id(3)] string Title) : Synapse;
