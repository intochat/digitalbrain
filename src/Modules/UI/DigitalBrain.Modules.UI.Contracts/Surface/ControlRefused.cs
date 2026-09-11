namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.control-refused")]
public sealed record ControlRefused(
    [property: Id(0)] string SurfaceKey,
    [property: Id(1)] string ControlId,
    [property: Id(2)] string Intent,
    [property: Id(3)] string Reason);
