namespace DigitalBrain.Conversations.Mcp;

public sealed record ChatChartOfferResult(
    string Title,
    IReadOnlyList<ChatChartPointResult> Points,
    string ChartKind);

