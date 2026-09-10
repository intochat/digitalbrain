using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Opens a scene and returns the newly keyed components.</summary>
[GenerateSerializer]
[Alias("ui.open-surface")]
public sealed record OpenSurface(
    CommandId Id,
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string Title,
    [property: Id(2)] SurfaceComponent? Root = null) : Command(Id);
