using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Flutter;

[GenerateSerializer]
[Alias("flutter.control-activated")]
[Description("A Flutter control was activated")]
public sealed record ControlActivated(
    [property: Id(0)] string SceneKey,
    [property: Id(1)] string ControlId,
    [property: Id(2)] string Intent) : Synapse;
