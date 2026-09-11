using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-open-receipt")]
public sealed record SurfaceOpenReceipt(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Fingerprint,
    [property: Id(2)] IReadOnlyList<SurfaceComponent> AddedComponents);
