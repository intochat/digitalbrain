namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.binding")]
public sealed record AppBinding(
    [property: Id(0)] string Source,
    [property: Id(1)] string Signal,
    [property: Id(2)] string Target);
