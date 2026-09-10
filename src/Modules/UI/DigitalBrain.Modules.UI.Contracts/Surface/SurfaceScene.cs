namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-scene")]
public sealed record SurfaceScene(
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string Title,
    [property: Id(2)] SurfaceComponent? Root = null);
