namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-state")]
public sealed record SurfaceState(
    [property: Id(0)] IReadOnlyList<SurfaceScene> Scenes,
    [property: Id(1)] IReadOnlyList<ActivityView>? Activities = null,
    [property: Id(2)] IReadOnlyList<SurfaceOpenReceipt>? OpenReceipts = null);
