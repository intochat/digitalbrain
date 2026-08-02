using DigitalBrain.Abstractions;

namespace DigitalBrain.Shell;

[GenerateSerializer]
[Alias("flutter.open-scene")]
public sealed record OpenScene(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string SceneKey,
    [property: Id(2)] string Title);
