using System.Text.Json;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.surface-component")]
public sealed record SurfaceComponent(
    [property: Id(0)] string Kind,
    [property: Id(1)] string? Key = null,
    [property: Id(2)] JsonElement? Properties = null,
    [property: Id(3)] IReadOnlyList<SurfaceComponent>? Children = null);
