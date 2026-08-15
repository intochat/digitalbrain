namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chat-chart-offer")]
public sealed record ChatChartPoint(
    [property: Id(0)] string Label,
    [property: Id(1)] double Value);