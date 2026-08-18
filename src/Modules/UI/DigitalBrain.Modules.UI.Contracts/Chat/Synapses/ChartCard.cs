namespace DigitalBrain.Chat;

[GenerateSerializer]
[Alias("ui.chart-card")]
public sealed record ChartCard([property: Id(0)] string Title) : Synapse;
