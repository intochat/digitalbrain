using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

/// <summary>Activates a button on the current surface scene.</summary>
[GenerateSerializer]
[Alias("ui.activate-control")]
public sealed record ActivateControl(
    CommandId Id,
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string ControlId,
    [property: Id(2)] string Intent) : Command(Id);
