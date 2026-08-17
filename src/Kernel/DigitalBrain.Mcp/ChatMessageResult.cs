namespace DigitalBrain.Mcp;

internal sealed record ChatMessageResult(
    string Chat,
    string CommandId,
    string CorrelationId,
    string Response,
    long Sequence,
    DateTimeOffset Timestamp,
    IReadOnlyList<ChatButtonOfferResult>? Buttons = null,
    IReadOnlyList<ChatChartOfferResult>? Charts = null);

