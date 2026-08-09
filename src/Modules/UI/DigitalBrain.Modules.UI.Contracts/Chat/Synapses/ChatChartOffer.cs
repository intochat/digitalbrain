namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.chat-chart-offer")]
public sealed record ChatChartPoint(
    [property: Id(0)] string Label,
    [property: Id(1)] double Value);

[GenerateSerializer]
[Alias("ui.chat-chart")]
public sealed record ChatChartOffer(
    [property: Id(0)] string Title,
    [property: Id(1)] ChatChartPoint[] Points,
    [property: Id(2)] string ChartKind = "bar");
