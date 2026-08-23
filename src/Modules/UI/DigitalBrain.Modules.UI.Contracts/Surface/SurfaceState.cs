namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-state")]
public sealed record SurfaceState(
    [property: Id(0)] IReadOnlyList<SurfaceScene> Scenes);

[GenerateSerializer]
[Alias("ui.surface-scene")]
public sealed record SurfaceScene(
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string Title);
