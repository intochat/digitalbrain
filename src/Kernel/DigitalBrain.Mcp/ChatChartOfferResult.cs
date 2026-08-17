namespace DigitalBrain.Mcp;

internal sealed record ChatChartOfferResult(
    string Title,
    IReadOnlyList<ChatChartPointResult> Points,
    string ChartKind);

