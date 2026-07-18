namespace DigitalBrain.Runtime.Ui;

public sealed record TaskManagerCardPayload(
    IReadOnlyList<TaskManagerRow> Tasks,
    TaskManagerTotals Totals);

public sealed record TaskManagerRow(
    string CorrelationId,
    string ShortHash,
    string OriginNeuron,
    string OriginIcon,
    long AgeMs,
    int EdgeCount,
    string Status,
    IReadOnlyList<string> Participating);

public sealed record TaskManagerTotals(int Active, int Completed, int Failed);
