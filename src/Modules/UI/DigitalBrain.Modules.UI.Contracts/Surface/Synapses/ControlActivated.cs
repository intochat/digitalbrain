using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.control-activated")]
[Description("A surface control was activated")]
public sealed record ControlActivated(
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string ControlId,
    [property: Id(2)] string Intent) : Synapse;
