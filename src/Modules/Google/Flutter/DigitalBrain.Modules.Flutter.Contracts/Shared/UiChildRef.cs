namespace DigitalBrain.Flutter;

[GenerateSerializer, Alias("ui.child-ref")]
public sealed record UiChildRef(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Name);