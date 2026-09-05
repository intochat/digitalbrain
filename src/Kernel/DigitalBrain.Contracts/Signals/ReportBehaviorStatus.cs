namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.report-behavior-status")]
public sealed record ReportBehaviorStatus(
    [property: Id(0)] string Name,
    [property: Id(1)] Guid Revision,
    [property: Id(2)] BehaviorStatus Status,
    [property: Id(3)] string Summary,
    [property: Id(4)] IReadOnlyList<string> Diagnostics) : Signal;
