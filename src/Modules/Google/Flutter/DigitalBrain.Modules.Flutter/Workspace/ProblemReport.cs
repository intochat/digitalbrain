namespace DigitalBrain.Flutter.Workspace;

// A "Report a problem" entry always carries the intent id it was raised from, so support can join
// it to the intent's usage, trace and statement line.
[GenerateSerializer, Alias("intochat.problem-report")]
public sealed record ProblemReport(
    [property: Id(0)] string IntentId,
    [property: Id(1)] string WorkspaceId,
    [property: Id(2)] string Message,
    [property: Id(3)] DateTimeOffset ReportedAt);
