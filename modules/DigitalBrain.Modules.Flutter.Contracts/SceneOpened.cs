using DigitalBrain.Abstractions;

namespace DigitalBrain.Flutter;

[GenerateSerializer]
[Alias("flutter.scene-opened")]
public sealed record SceneOpened(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Shell,
    [property: Id(2)] string SceneKey,
    [property: Id(3)] string Title) : Synapse;
