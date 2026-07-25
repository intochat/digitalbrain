namespace DigitalBrain.Tasks.Tests;

[GenerateSerializer]
[Alias("tasks.tests.goal")]
public sealed record TestGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.stale-probe-goal")]
public sealed record StaleProbeGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.retryable-failure-goal")]
public sealed record RetryableFailureGoal(
    [property: Id(0)] string Label) : Goal;

[GenerateSerializer]
[Alias("tasks.tests.result")]
public sealed record TestResult(
    [property: Id(0)] string Label) : Result;

[GenerateSerializer]
[Alias("tasks.tests.failure")]
public sealed record TestFailure(
    [property: Id(0)] string Label) : Failure;
