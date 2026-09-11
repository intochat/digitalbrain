namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.activity-changed")]
public sealed record ActivityChanged(
    [property: Id(0)] ActivityView Activity);
