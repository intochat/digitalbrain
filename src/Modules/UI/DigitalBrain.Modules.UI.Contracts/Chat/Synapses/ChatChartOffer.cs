namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chat-chart")]
public sealed record ChatChartOffer(
    [property: Id(0)] string Title,
    [property: Id(1)] ChatChartPoint[] Points,
    [property: Id(2)] string ChartKind = "bar");