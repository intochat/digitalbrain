using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.component-added")]
public sealed record ComponentAdded(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Surface,
    [property: Id(2)] string SurfaceKey,
    [property: Id(3)] SurfaceComponent Component,
    [property: Id(4)] string EventId);
