namespace DigitalBrain.Automations;

public enum JobOutcome
{
    Succeeded = 0,
    Failed = 1,
    SkippedBudget = 2,
}

// Idempotency key: (AutomationId, ScheduledFor). Paid or side-effect steps are never retried automatically.
[GenerateSerializer, Alias("automations.job-run")]
public sealed record JobRun
{
    [Id(0)] public required string AutomationId { get; init; }
    [Id(1)] public required DateTimeOffset ScheduledFor { get; init; }
    [Id(2)] public required string WorkspaceId { get; init; }
    [Id(3)] public required string IntentId { get; init; }
    [Id(4)] public required JobOutcome Outcome { get; init; }
    [Id(5)] public string? FirstError { get; init; }
}
